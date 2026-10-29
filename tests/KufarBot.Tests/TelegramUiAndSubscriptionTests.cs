using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.App.Telegram;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace KufarBot.Tests;

public class TelegramUiAndSubscriptionTests
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
    public async Task SaveSearchAsync_CreatesSearchAndSharedPollingFeed()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var subService = new SearchSubscriptionService(dbContext, NullLogger<SearchSubscriptionService>.Instance);

        long userId = 10001;
        dbContext.Users.Add(new UserEntity { Id = userId, Username = "user1", CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var search = await subService.SaveSearchAsync(
            userId: userId,
            subcategoryId: 16010,
            subcategoryTitle: "Комплектующие",
            cityId: 7,
            cityName: "Минск",
            minPriceByn: 100m,
            maxPriceByn: 500m
        );

        Assert.NotNull(search);
        Assert.Equal(userId, search.UserId);
        Assert.Equal(16010, search.SubcategoryId);
        Assert.Equal(7, search.CityId);
        Assert.False(search.IsPaused);

        // Verify shared PollingFeed was created
        var feed = await dbContext.PollingFeeds.FirstOrDefaultAsync(f => f.SubcategoryId == 16010 && f.CityId == 7);
        Assert.NotNull(feed);
    }

    [Fact]
    public async Task SaveSearchAsync_IdempotentOnDuplicateCall_ReusesExistingSearch()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var subService = new SearchSubscriptionService(dbContext, NullLogger<SearchSubscriptionService>.Instance);

        long userId = 10002;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var s1 = await subService.SaveSearchAsync(userId, 16010, "Title", 7, "Минск", 50m, 150m);
        var s2 = await subService.SaveSearchAsync(userId, 16010, "Title", 7, "Минск", 50m, 150m);

        Assert.Equal(s1.Id, s2.Id);

        var totalSearches = await dbContext.UserSearches.CountAsync();
        Assert.Equal(1, totalSearches);

        var totalFeeds = await dbContext.PollingFeeds.CountAsync();
        Assert.Equal(1, totalFeeds);
    }

    [Fact]
    public async Task MultipleUsersSameFeed_ReusesSharedFeed_AndPreservesFeedOnSingleUserDeletion()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var subService = new SearchSubscriptionService(dbContext, NullLogger<SearchSubscriptionService>.Instance);

        long user1 = 101;
        long user2 = 102;
        dbContext.Users.Add(new UserEntity { Id = user1, CreatedAtUtc = DateTime.UtcNow });
        dbContext.Users.Add(new UserEntity { Id = user2, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var search1 = await subService.SaveSearchAsync(user1, 16010, "Title", 7, "Минск", 50m, 100m);
        var search2 = await subService.SaveSearchAsync(user2, 16010, "Title", 7, "Минск", 120m, 300m);

        // Feed must be shared
        var feedCount = await dbContext.PollingFeeds.CountAsync(f => f.SubcategoryId == 16010 && f.CityId == 7);
        Assert.Equal(1, feedCount);

        // User1 deletes their search
        bool deleted = await subService.DeleteSearchAsync(search1.Id, user1);
        Assert.True(deleted);

        // Feed must STILL exist because User2 is still subscribed to this subcategory & city
        var feedStillExists = await dbContext.PollingFeeds.AnyAsync(f => f.SubcategoryId == 16010 && f.CityId == 7);
        Assert.True(feedStillExists);

        // User2 deletes their search
        bool deleted2 = await subService.DeleteSearchAsync(search2.Id, user2);
        Assert.True(deleted2);

        // Now no searches reference this feed -> Feed is cleaned up
        var feedCleanedUp = await dbContext.PollingFeeds.AnyAsync(f => f.SubcategoryId == 16010 && f.CityId == 7);
        Assert.False(feedCleanedUp);
    }

    [Fact]
    public async Task OwnershipChecks_PreventAccessOrModificationByOtherUsers()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var subService = new SearchSubscriptionService(dbContext, NullLogger<SearchSubscriptionService>.Instance);

        long ownerId = 2001;
        long attackerId = 2002;
        dbContext.Users.Add(new UserEntity { Id = ownerId, CreatedAtUtc = DateTime.UtcNow });
        dbContext.Users.Add(new UserEntity { Id = attackerId, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var search = await subService.SaveSearchAsync(ownerId, 16010, "Title", null, null, null, null);

        // Attacker attempts to get search
        var fetchedByAttacker = await subService.GetSearchByIdAsync(search.Id, attackerId);
        Assert.Null(fetchedByAttacker);

        // Attacker attempts to pause search
        bool pauseResult = await subService.TogglePauseAsync(search.Id, attackerId, isPaused: true);
        Assert.False(pauseResult);

        // Attacker attempts to delete search
        bool deleteResult = await subService.DeleteSearchAsync(search.Id, attackerId);
        Assert.False(deleteResult);

        // Owner can pause and get search
        bool ownerPause = await subService.TogglePauseAsync(search.Id, ownerId, isPaused: true);
        Assert.True(ownerPause);

        var fetchedByOwner = await subService.GetSearchByIdAsync(search.Id, ownerId);
        Assert.NotNull(fetchedByOwner);
        Assert.True(fetchedByOwner.IsPaused);
    }

    [Fact]
    public async Task DeleteSearch_CancelsPendingOutboxNotifications()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var subService = new SearchSubscriptionService(dbContext, NullLogger<SearchSubscriptionService>.Instance);

        long userId = 3001;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var search = await subService.SaveSearchAsync(userId, 16010, "Title", null, null, null, null);

        // Add pending outbox item for this search
        dbContext.NotificationOutbox.Add(new NotificationOutboxEntity
        {
            UserId = userId,
            SearchId = search.Id,
            AdId = 99999,
            Status = 0, // Pending
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        // Delete search
        await subService.DeleteSearchAsync(search.Id, userId);

        var outboxItem = await dbContext.NotificationOutbox.FirstAsync(o => o.AdId == 99999);
        Assert.Equal(4, outboxItem.Status); // DeadLetter / Cancelled
        Assert.Equal("Search deleted by user", outboxItem.LastError);
    }

    [Fact]
    public async Task CityCatalog_PreservesLegacyCityMappings()
    {
        Assert.Equal(7, CityCatalog.CitiesByName["Минск"]);
        Assert.Equal(5, CityCatalog.CitiesByName["Гомель"]);
        Assert.Equal(13, CityCatalog.CitiesByName["Могилев"]);
        Assert.Equal(18, CityCatalog.CitiesByName["Витебск"]);
        Assert.Equal(9, CityCatalog.CitiesByName["Гродно"]);
        Assert.Equal(1, CityCatalog.CitiesByName["Брест"]);

        Assert.Equal("Минск", CityCatalog.GetCityName(7));
        Assert.Equal("Брест", CityCatalog.GetCityName(1));
    }

    [Fact]
    public async Task HandleUpdateAsync_StartCommand_IsIdempotentAndRegistersUser()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource={dbName};Mode=Memory;Cache=Shared")
            .Options;

        using var keepAliveConn = new Microsoft.Data.Sqlite.SqliteConnection($"DataSource={dbName};Mode=Memory;Cache=Shared");
        keepAliveConn.Open();

        using (var initContext = new AppDbContext(options))
        {
            initContext.Database.EnsureCreated();
        }

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddDbContext<AppDbContext>(opt => opt.UseSqlite($"DataSource={dbName};Mode=Memory;Cache=Shared"));
        serviceCollection.AddScoped<ISearchSubscriptionService, SearchSubscriptionService>();
        serviceCollection.AddScoped<ICategoryService>(_ => Mock.Of<ICategoryService>());
        serviceCollection.AddScoped<KufarBot.Infrastructure.Analytics.IUsageAnalytics, KufarBot.Infrastructure.Analytics.UsageAnalytics>();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var botClientMock = new Mock<ITelegramBotClient>();
        var sessionStore = new UserSessionStore();

        var engine = new BotEngine(
            serviceProvider,
            botClientMock.Object,
            sessionStore,
            NullLogger<BotEngine>.Instance
        );

        var update = new Update
        {
            Message = new Message
            {
                Id = 1,
                Date = DateTime.UtcNow,
                Chat = new Chat { Id = 55501, Type = ChatType.Private },
                From = new User { Id = 55501, Username = "testguy", FirstName = "Tester" },
                Text = "/start"
            }
        };

        // First call
        await engine.HandleUpdateAsync(botClientMock.Object, update, CancellationToken.None);

        using (var verifyContext = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>())
        {
            var user = await verifyContext.Users.FirstOrDefaultAsync(u => u.Id == 55501);
            Assert.NotNull(user);
            Assert.Equal("testguy", user.Username);
        }

        // Second call (idempotency check)
        await engine.HandleUpdateAsync(botClientMock.Object, update, CancellationToken.None);

        using (var verifyContext = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>())
        {
            var userCount = await verifyContext.Users.CountAsync(u => u.Id == 55501);
            Assert.Equal(1, userCount);
        }
    }
}
