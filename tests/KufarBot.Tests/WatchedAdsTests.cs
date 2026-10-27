using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;
using KufarBot.Infrastructure.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KufarBot.Tests;

/// <summary>
/// eph-8b: favorites — ad details via search-api/v2/item/{id}/rendered, price-drop
/// tracking, one-time disappearance notice.
/// </summary>
public class WatchedAdsTests
{
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_factory(request));
        }
    }

    private const string ItemJson = """
        {
          "result": {
            "ad_id": 100,
            "subject": "MSI GTX 1060",
            "body": "RARE",
            "price_byn": "1000",
            "list_time": "2026-01-12T10:00:00Z",
            "ad_link": "https://www.kufar.by/item/100",
            "ad_parameters": [
              { "p": "region", "v": "Минск", "pl": "Регион" },
              { "p": "category", "v": 16010, "pl": "Категория" }
            ],
            "images": [
              { "id": "img01", "path": "adim1/7dc5fc02-b93a-4624-9d04-4adab2a08d4a.jpg" }
            ]
          }
        }
        """;

    [Fact]
    public async Task GetAdDetailsAsync_NormalizesResult()
    {
        var handler = new StubHttpHandler(request =>
        {
            Assert.Contains("/item/100/rendered", request.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ItemJson, Encoding.UTF8, "application/json") };
        });

        var client = new KufarApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.kufar.by/") });

        var ad = await client.GetAdDetailsAsync(100, CancellationToken.None);

        Assert.NotNull(ad);
        Assert.Equal(100L, ad!.Id);
        Assert.Equal("MSI GTX 1060", ad.Title);
        Assert.Equal(10.00m, ad.PriceByn);
        Assert.Equal("Минск", ad.Location);
        Assert.Equal("https://www.kufar.by/item/100", ad.AdLink);
        Assert.NotEmpty(ad.AllImageUrls);
    }

    [Fact]
    public async Task GetAdDetailsAsync_ReturnsNullOn404()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = new KufarApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.kufar.by/") });

        var ad = await client.GetAdDetailsAsync(999999, CancellationToken.None);

        Assert.Null(ad);
    }

    // ---------------- WatchedAdsWorker ----------------

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

    private (WatchedAdsWorker Worker, Mock<IKufarApiClient> Client) CreateWorker(AppDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dbContext);

        var mockClient = new Mock<IKufarApiClient>();
        services.AddSingleton(mockClient.Object);

        var options = new WatchedAdsOptions { RecheckIntervalMinutes = 0, IntervalSeconds = 1, BatchSize = 10 };
        var worker = new WatchedAdsWorker(services.BuildServiceProvider(), NullLogger<WatchedAdsWorker>.Instance, options);

        return (worker, mockClient);
    }

    private static WatchedAdEntity Watched(long price)
    {
        return new WatchedAdEntity
        {
            UserId = 1,
            AdId = 100,
            Title = "MSI GTX 1060",
            AdLink = "https://www.kufar.by/item/100",
            ImageUrl = "https://rms.kufar.by/v1/a.jpg",
            Location = "Минск",
            LastPriceByn = price,
            LastCheckedUtc = DateTime.UtcNow.AddHours(-3),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-3)
        };
    }

    private static Advertisement Details(long price) => new()
    {
        Id = 100,
        Title = "MSI GTX 1060",
        PriceByn = price,
        Location = "Минск",
        AdLink = "https://www.kufar.by/item/100",
        ListTimeUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task ProcessBatchAsync_PriceDropped_EnqueuesPriceDropNotify_Once()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (worker, mockClient) = CreateWorker(dbContext);

        dbContext.Users.Add(new UserEntity { Id = 1, CreatedAtUtc = DateTime.UtcNow });
        dbContext.WatchedAds.Add(Watched(100));
        await dbContext.SaveChangesAsync();

        // two checks in a row — the drop triggers a notification only the first time
        mockClient.SetupSequence(c => c.GetAdDetailsAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Details(80))
            .ReturnsAsync(Details(80));

        await worker.ProcessBatchAsync(CancellationToken.None);
        await worker.ProcessBatchAsync(CancellationToken.None);

        var outbox = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(outbox);

        var payload = System.Text.Json.JsonSerializer.Deserialize<OutboxPayload>(outbox[0].PayloadJson)!;
        Assert.Equal(OutboxPayload.KindPriceDrop, payload.Kind);
        Assert.Equal("80 BYN", payload.PriceFormatted);
        Assert.Equal("100 BYN", payload.OldPriceFormatted);

        var watched = await dbContext.WatchedAds.SingleAsync();
        Assert.Equal(80m, watched.LastPriceByn);
    }

    [Fact]
    public async Task ProcessBatchAsync_AdMissing_NotifiesOnce_AndClearsIfBack()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (worker, mockClient) = CreateWorker(dbContext);

        dbContext.Users.Add(new UserEntity { Id = 1, CreatedAtUtc = DateTime.UtcNow });
        dbContext.WatchedAds.Add(Watched(100));
        await dbContext.SaveChangesAsync();

        mockClient.SetupSequence(c => c.GetAdDetailsAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Advertisement?)null)   // removed
            .ReturnsAsync((Advertisement?)null)   // still withdrawn: no duplicate
            .ReturnsAsync(Details(100));          // back online: cleared

        await worker.ProcessBatchAsync(CancellationToken.None);
        await worker.ProcessBatchAsync(CancellationToken.None);
        await worker.ProcessBatchAsync(CancellationToken.None);

        var drops = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(drops);

        var payload = System.Text.Json.JsonSerializer.Deserialize<OutboxPayload>(drops[0].PayloadJson)!;
        Assert.Equal(OutboxPayload.KindAdMissing, payload.Kind);

        var watched = await dbContext.WatchedAds.SingleAsync();
        Assert.False(watched.NotifiedMissing);
        Assert.Equal(100m, watched.LastPriceByn);
    }

    [Fact]
    public async Task ProcessBatchAsync_PriceIncreasedOrEqual_NoNotification()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (worker, mockClient) = CreateWorker(dbContext);

        dbContext.Users.Add(new UserEntity { Id = 1, CreatedAtUtc = DateTime.UtcNow });
        dbContext.WatchedAds.Add(Watched(100));
        await dbContext.SaveChangesAsync();

        mockClient.Setup(c => c.GetAdDetailsAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Details(120));

        await worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Empty(await dbContext.NotificationOutbox.ToListAsync());
    }

    [Fact]
    public async Task ProcessBatchAsync_ApiError_IsSwallowedAndSkipped()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var (worker, mockClient) = CreateWorker(dbContext);

        dbContext.Users.Add(new UserEntity { Id = 1, CreatedAtUtc = DateTime.UtcNow });
        dbContext.WatchedAds.Add(Watched(100));
        await dbContext.SaveChangesAsync();

        mockClient.Setup(c => c.GetAdDetailsAsync(100, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var processed = await worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Empty(await dbContext.NotificationOutbox.ToListAsync());
    }

    [Fact]
    public void OutboxPayload_KindConstants_AreStable()
    {
        Assert.Equal("new_ad", OutboxPayload.KindNewAd);
        Assert.Equal("fav_price_drop", OutboxPayload.KindPriceDrop);
        Assert.Equal("fav_missing", OutboxPayload.KindAdMissing);
    }
}
