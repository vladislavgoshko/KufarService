using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;
using KufarBot.Infrastructure.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KufarBot.Tests;

public class FeedPollingWorkerTests
{
    private AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new AppDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task PollSingleFeedAsync_DiscoversNewAds_CreatesOutboxAndDeliveredAds()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 999;
        dbContext.Users.Add(new UserEntity
        {
            Id = userId,
            Username = "pollinguser",
            CreatedAtUtc = DateTime.UtcNow
        });

        dbContext.UserSearches.Add(new UserSearchEntity
        {
            Id = 10,
            UserId = userId,
            SubcategoryId = 16010,
            SubcategoryTitle = "Комплектующие",
            CityId = 7,
            CityName = "Минск",
            MinPriceByn = 50m,
            MaxPriceByn = 300m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var baseTime = new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc);
        dbContext.PollingFeeds.Add(new PollingFeedEntity
        {
            Id = 1,
            SubcategoryId = 16010,
            CityId = 7,
            LastSeenAdTimeUtc = baseTime,
            LastSeenAdIdsJson = "[100]"
        });

        await dbContext.SaveChangesAsync();

        var newAd = new Advertisement
        {
            Id = 101,
            Title = "MSI GPU",
            PriceByn = 150.00m,
            Location = "Минск",
            AdLink = "https://kufar.by/item/101",
            ListTimeUtc = baseTime.AddMinutes(5)
        };

        var mockClient = new Mock<IKufarApiClient>();
        mockClient.Setup(c => c.SearchAsync(It.IsAny<KufarSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KufarSearchResult(new List<Advertisement> { newAd }, null, 1));

        var worker = new FeedPollingWorker(
            Mock.Of<IServiceProvider>(),
            NullLogger<FeedPollingWorker>.Instance
        );

        var feed = await dbContext.PollingFeeds.FirstAsync();

        await worker.PollSingleFeedAsync(dbContext, mockClient.Object, feed, CancellationToken.None);

        var outboxItems = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(outboxItems);
        Assert.Equal(userId, outboxItems[0].UserId);
        Assert.Equal(101L, outboxItems[0].AdId);
        Assert.Equal(0, outboxItems[0].Status);

        var deliveredAds = await dbContext.DeliveredAds.ToListAsync();
        Assert.Single(deliveredAds);
        Assert.Equal(101L, deliveredAds[0].AdId);

        Assert.Equal(newAd.ListTimeUtc, feed.LastSeenAdTimeUtc);
        Assert.Contains("101", feed.LastSeenAdIdsJson);
    }

    [Fact]
    public async Task PollSingleFeedAsync_RespectsPriceBoundaries_AndUserDeduplication()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 888;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        dbContext.UserSearches.Add(new UserSearchEntity
        {
            Id = 1,
            UserId = userId,
            SubcategoryId = 16010,
            MinPriceByn = 10m,
            MaxPriceByn = 100m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.UserSearches.Add(new UserSearchEntity
        {
            Id = 2,
            UserId = userId,
            SubcategoryId = 16010,
            MinPriceByn = 50m,
            MaxPriceByn = 200m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var baseTime = new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc);
        dbContext.PollingFeeds.Add(new PollingFeedEntity
        {
            Id = 1,
            SubcategoryId = 16010,
            CityId = null,
            LastSeenAdTimeUtc = baseTime,
            LastSeenAdIdsJson = "[]"
        });

        await dbContext.SaveChangesAsync();

        var adTooCheap = new Advertisement { Id = 201, Title = "Cheap", PriceByn = 5m, Location = "Minsk", AdLink = "link1", ListTimeUtc = baseTime.AddHours(1) };
        var adMatching = new Advertisement { Id = 202, Title = "Match", PriceByn = 75m, Location = "Minsk", AdLink = "link2", ListTimeUtc = baseTime.AddHours(2) };
        var adTooExpensive = new Advertisement { Id = 203, Title = "Expensive", PriceByn = 500m, Location = "Minsk", AdLink = "link3", ListTimeUtc = baseTime.AddHours(3) };

        var mockClient = new Mock<IKufarApiClient>();
        mockClient.Setup(c => c.SearchAsync(It.IsAny<KufarSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KufarSearchResult(new List<Advertisement> { adTooCheap, adMatching, adTooExpensive }, null, 3));

        var worker = new FeedPollingWorker(Mock.Of<IServiceProvider>(), NullLogger<FeedPollingWorker>.Instance);
        var feed = await dbContext.PollingFeeds.FirstAsync();

        await worker.PollSingleFeedAsync(dbContext, mockClient.Object, feed, CancellationToken.None);

        var outboxItems = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(outboxItems);
        Assert.Equal(202L, outboxItems[0].AdId);
        Assert.Equal(userId, outboxItems[0].UserId);
    }

    [Fact]
    public async Task PollSingleFeedAsync_AttachesSearchesOnlyToTheirOwnFeed()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 777;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        // One "All Belarus" search (CityId = null) and one city search (CityId = 7).
        dbContext.UserSearches.AddRange(
            new UserSearchEntity
            {
                Id = 1,
                UserId = userId,
                SubcategoryId = 16010,
                CityId = null,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            },
            new UserSearchEntity
            {
                Id = 2,
                UserId = userId,
                SubcategoryId = 16010,
                CityId = 7,
                CityName = "Минск",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

        var baseTime = new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc);

        // 1) The "All Belarus" feed must serve only the CityId == null search.
        dbContext.PollingFeeds.Add(new PollingFeedEntity
        {
            Id = 1,
            SubcategoryId = 16010,
            CityId = null,
            LastSeenAdTimeUtc = baseTime,
            LastSeenAdIdsJson = "[]"
        });
        await dbContext.SaveChangesAsync();

        var ad = new Advertisement
        {
            Id = 301,
            Title = "Any city ad",
            PriceByn = 100m,
            Location = "Гомель",
            AdLink = "link301",
            ListTimeUtc = baseTime.AddMinutes(1)
        };

        var mockClient = new Mock<IKufarApiClient>();
        mockClient.Setup(c => c.SearchAsync(It.IsAny<KufarSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KufarSearchResult(new List<Advertisement> { ad }, null, 1));

        var worker = new FeedPollingWorker(Mock.Of<IServiceProvider>(), NullLogger<FeedPollingWorker>.Instance);
        var allBelarusFeed = await dbContext.PollingFeeds.FirstAsync(f => f.Id == 1);

        await worker.PollSingleFeedAsync(dbContext, mockClient.Object, allBelarusFeed, CancellationToken.None);

        var outboxItems = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(outboxItems); // Only SearchId=1 ("All Belarus") matched; the city search must NOT be served by this feed.
        Assert.Equal(1, outboxItems[0].SearchId);

        // The Kufar query must reflect the feed's own city scope.
        mockClient.Verify(c => c.SearchAsync(It.Is<KufarSearchCriteria>(cr => cr.AreaId == null), It.IsAny<CancellationToken>()), Times.Once);

        // 2) A city feed must serve only the city search, not the "All Belarus" search.
        dbContext.NotificationOutbox.RemoveRange(outboxItems);
        dbContext.DeliveredAds.RemoveRange(await dbContext.DeliveredAds.ToListAsync());
        dbContext.PollingFeeds.Add(new PollingFeedEntity
        {
            Id = 3,
            SubcategoryId = 16010,
            CityId = 7,
            LastSeenAdTimeUtc = baseTime,
            LastSeenAdIdsJson = "[]"
        });
        await dbContext.SaveChangesAsync();
        allBelarusFeed.LastSeenAdTimeUtc = DateTime.MaxValue; // freeze the general feed checkpoint
        await dbContext.SaveChangesAsync();

        var cityFeed = await dbContext.PollingFeeds.FirstAsync(f => f.Id == 3);
        await worker.PollSingleFeedAsync(dbContext, mockClient.Object, cityFeed, CancellationToken.None);

        var cityFeedOutbox = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(cityFeedOutbox); // Only SearchId=2 (city 7) matched.
        Assert.Equal(2, cityFeedOutbox[0].SearchId);
        mockClient.Verify(c => c.SearchAsync(It.Is<KufarSearchCriteria>(cr => cr.AreaId == 7), It.IsAny<CancellationToken>()), Times.Once);
    }
}
