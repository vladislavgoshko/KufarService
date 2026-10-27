using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Filters;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using KufarBot.App.Telegram;
using KufarBot.Infrastructure.Persistence;
using KufarBot.Infrastructure.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KufarBot.Tests;

/// <summary>
/// eph-8a: server-side filter parameters go into the Kufar search API query; the polling
/// feed identity is (subcategory, city, URL filter set).
/// Verified live API formats: cnd=2 | pse=1 | cct=v.or:11,5 | prc=r:100,2000.
/// </summary>
public class UrlFiltersTests
{
    private static SavedFilter Bool(string key, string urlName) => new()
    {
        Kind = SavedFilterKind.Bool, Key = key, UrlName = urlName, Title = key, Values = new[] { "1" }
    };

    private static SavedFilter Single(string key, string urlName, string value) => new()
    {
        Kind = SavedFilterKind.Single, Key = key, UrlName = urlName, Title = key, Values = new[] { value }
    };

    private static SavedFilter Multi(string key, string urlName, params string[] values) => new()
    {
        Kind = SavedFilterKind.Multi, Key = key, UrlName = urlName, Title = key, Values = values
    };

    private static SavedFilter Range(string key, string urlName, decimal? min, decimal? max) => new()
    {
        Kind = SavedFilterKind.Range, Key = key, UrlName = urlName, Title = key, Min = min, Max = max
    };

    private static SavedFilter Own(SavedFilterKind kind, string key) => new()
    {
        Kind = kind, Key = key, Title = key, Values = new[] { "значение" }
    };

    // ---------------- UrlFiltersBuilder ----------------

    [Fact]
    public void Build_SingleAndBool_MapUrlNameToValue()
    {
        var dict = UrlFiltersBuilder.Build(new[] { Single("condition", "cnd", "2"), Bool("possible_exchange", "pse") });

        Assert.Equal(2, dict.Count);
        Assert.Equal("2", dict["cnd"]);
        Assert.Equal("1", dict["pse"]);
    }

    [Fact]
    public void Build_Multi_UsesOrPrefixWithSortedValues()
    {
        var dict = UrlFiltersBuilder.Build(new[] { Multi("computers_component_type", "cct", "11", "5") });

        Assert.Equal("v.or:11,5", dict["cct"]);
    }

    [Fact]
    public void Build_Multi_SingleValueStillUsesOrPrefix()
    {
        var dict = UrlFiltersBuilder.Build(new[] { Multi("condition", "cnd", "1") });

        Assert.Equal("v.or:1", dict["cnd"]);
    }

    [Fact]
    public void Build_Range_EncodesRawMinAndMax()
    {
        var dict = UrlFiltersBuilder.Build(new[] { Range("price", "prc", 100m, 2000m) });

        Assert.Equal("r:100,2000", dict["prc"]);
    }

    [Fact]
    public void Build_Range_FillsMissingBounds()
    {
        var onlyMax = UrlFiltersBuilder.Build(new[] { Range("freq", "cdf", null, 7m) });
        Assert.Equal($"r:0,7", onlyMax["cdf"]);

        var onlyMin = UrlFiltersBuilder.Build(new[] { Range("freq", "cdf", 3m, null) });
        Assert.Equal($"r:3,{UrlFiltersBuilder.DefaultRangeUpper:0.########}", onlyMin["cdf"]);
    }

    [Fact]
    public void Build_OwnFiltersAndLegacyRowsWithoutUrlName_AreSkipped()
    {
        var dict = UrlFiltersBuilder.Build(new SavedFilter[]
        {
            Own(SavedFilterKind.KeywordsInclude, "own_kw_inc"),
            Own(SavedFilterKind.KeywordsExclude, "own_kw_exc"),
            Own(SavedFilterKind.SellerType, "own_seller"),
            Own(SavedFilterKind.MaxAgeHours, "own_age"),
            Own(SavedFilterKind.Query, "own_query"),
            new SavedFilter { Kind = SavedFilterKind.Single, Key = "condition", Title = "Состояние", Values = new[] { "2" } }, // no UrlName (legacy)
        });

        Assert.Empty(dict);
    }

    [Theory]
    [InlineData(new[] { "1", "2" }, new[] { "2", "1" })]
    public void CanonicalJson_IsStableAcrossValueOrder(string[] first, string[] second)
    {
        var a = UrlFiltersBuilder.CanonicalJson(UrlFiltersBuilder.Build(new[] { Multi("cct", "cct", first) }));
        var b = UrlFiltersBuilder.CanonicalJson(UrlFiltersBuilder.Build(new[] { Multi("cct", "cct", second) }));

        Assert.Equal(a, b);
        Assert.Contains("v.or:1,2", a);
    }

    [Fact]
    public void CanonicalJson_EmptySet_HasStableLegacyIdentity()
    {
        Assert.Equal("{}", UrlFiltersBuilder.CanonicalJson(UrlFiltersBuilder.Empty));
        Assert.Equal("{}", UrlFiltersBuilder.CanonicalJson(new Dictionary<string, string>()));
        Assert.Equal("{}", UrlFiltersBuilder.CanonicalJson(UrlFiltersBuilder.Parse(null)));
    }

    [Fact]
    public void Parse_RoundtripsCanonicalJson()
    {
        var source = UrlFiltersBuilder.Build(new[]
        {
            Single("condition", "cnd", "2"),
            Multi("computers_component_type", "cct", "11", "5"),
            Range("price", "prc", 100m, 2000m),
        });
        string json = UrlFiltersBuilder.CanonicalJson(source);
        var parsed = UrlFiltersBuilder.Parse(json);

        Assert.True(UrlFiltersBuilder.CanonicalJson(parsed) == json);
        Assert.Equal("2", parsed["cnd"]);
        Assert.Equal("v.or:11,5", parsed["cct"]);
        Assert.Equal("r:100,2000", parsed["prc"]);
    }

    // ---------------- KufarUrlBuilder ----------------

    [Fact]
    public void BuildSearchUrl_AppendsUrlFiltersSortedAndEscaped()
    {
        var criteria = new KufarSearchCriteria
        {
            CategoryId = 16010,
            Size = 50,
            UrlFilters = new Dictionary<string, string> { ["cct"] = "v.or:11,5", ["cnd"] = "2", ["prc"] = "r:100,2000" }
        };

        string url = KufarUrlBuilder.BuildSearchUrl(criteria);

        Assert.Contains("cat=16010", url);
        Assert.Contains("cnd=2", url);
        Assert.Contains("cct=v.or%3A11%2C5", url);
        Assert.Contains("prc=r%3A100%2C2000", url);
        // sorted alphabetically: cct, cnd, prc
        Assert.True(url.IndexOf("cct=", StringComparison.Ordinal) < url.IndexOf("cnd=", StringComparison.Ordinal));
        Assert.True(url.IndexOf("cnd=", StringComparison.Ordinal) < url.IndexOf("prc=", StringComparison.Ordinal));
    }

    // ---------------- Feed identity ----------------

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
    public async Task PollSingleFeedAsync_FiltersFeedAttachesOnlySearchesWithSameUrlFilterSet()
    {
        await using var dbContext = CreateInMemoryDbContext();

        const long matchedUserId = 1;
        const long legacyUserId = 2;
        dbContext.Users.AddRange(
            new UserEntity { Id = matchedUserId, CreatedAtUtc = DateTime.UtcNow },
            new UserEntity { Id = legacyUserId, CreatedAtUtc = DateTime.UtcNow });

        // same server filter, but only the first row carries UrlName (legacy rows do not)
        string modernFilters = SavedFilter.Serialize(new[]
        {
            new SavedFilter { Kind = SavedFilterKind.Single, Key = "condition", UrlName = "cnd", Title = "Состояние", Values = new[] { "2" } }
        });
        string legacyFilters = SavedFilter.Serialize(new[]
        {
            new SavedFilter { Kind = SavedFilterKind.Single, Key = "condition", Title = "Состояние", Values = new[] { "2" } }
        });

        dbContext.UserSearches.Add(new UserSearchEntity
        {
            Id = 10,
            UserId = matchedUserId,
            SubcategoryId = 16010,
            CityId = null,
            FiltersJson = modernFilters,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        dbContext.UserSearches.Add(new UserSearchEntity
        {
            Id = 11,
            UserId = legacyUserId,
            SubcategoryId = 16010,
            CityId = null,
            FiltersJson = legacyFilters,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var baseTime = new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc);
        dbContext.PollingFeeds.Add(new PollingFeedEntity
        {
            Id = 1,
            SubcategoryId = 16010,
            CityId = null,
            UrlFiltersJson = UrlFiltersBuilder.CanonicalJson(UrlFiltersBuilder.Build(SavedFilter.Deserialize(modernFilters))),
            LastSeenAdTimeUtc = baseTime,
            LastSeenAdIdsJson = "[100]"
        });
        await dbContext.SaveChangesAsync();

        var ad = new Advertisement
        {
            Id = 101,
            Title = "MSI GPU",
            PriceByn = 150m,
            Location = "Минск",
            AdLink = "https://kufar.by/item/101",
            ListTimeUtc = baseTime.AddMinutes(5),
            Parameters = new List<AdParameter> { new("condition", "2") }
        };

        KufarSearchCriteria? capturedCriteria = null;
        var mockClient = new Mock<IKufarApiClient>();
        mockClient.Setup(c => c.SearchAsync(It.IsAny<KufarSearchCriteria>(), It.IsAny<CancellationToken>()))
            .Callback((KufarSearchCriteria c, CancellationToken _) => capturedCriteria = c)
            .ReturnsAsync(new KufarSearchResult(new List<Advertisement> { ad }, null, 1));

        var worker = new FeedPollingWorker(Mock.Of<IServiceProvider>(), NullLogger<FeedPollingWorker>.Instance);
        var feed = await dbContext.PollingFeeds.FirstAsync();
        await worker.PollSingleFeedAsync(dbContext, mockClient.Object, feed, CancellationToken.None);

        // The ad is fetched with the URL filters of the feed...
        Assert.NotNull(capturedCriteria);
        Assert.Equal("2", capturedCriteria!.UrlFilters!["cnd"]);

        // ...and delivered only to the search whose URL filter set is identical
        var outbox = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(outbox);
        Assert.Equal(matchedUserId, outbox[0].UserId);
    }

    [Fact]
    public async Task PollSingleFeedAsync_ApiFallsBackToBareFeed_WhenUrlFiltersAreRejected()
    {
        await using var dbContext = CreateInMemoryDbContext();

        const long userId = 1;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });
        dbContext.UserSearches.Add(new UserSearchEntity
        {
            Id = 10,
            UserId = userId,
            SubcategoryId = 16010,
            CityId = null,
            FiltersJson = SavedFilter.Serialize(new[]
            {
                new SavedFilter { Kind = SavedFilterKind.Single, Key = "condition", UrlName = "cnd", Title = "Состояние", Values = new[] { "2" } }
            }),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var baseTime = new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc);
        dbContext.PollingFeeds.Add(new PollingFeedEntity
        {
            Id = 1,
            SubcategoryId = 16010,
            CityId = null,
            UrlFiltersJson = UrlFiltersBuilder.CanonicalJson(new Dictionary<string, string> { ["cnd"] = "2" }),
            LastSeenAdTimeUtc = baseTime,
            LastSeenAdIdsJson = "[100]"
        });
        await dbContext.SaveChangesAsync();

        var ad = new Advertisement
        {
            Id = 101,
            Title = "MSI GPU",
            PriceByn = 150m,
            Location = "Минск",
            AdLink = "https://kufar.by/item/101",
            ListTimeUtc = baseTime.AddMinutes(5),
            Parameters = new List<AdParameter> { new("condition", "2") }
        };

        var calls = new List<KufarSearchCriteria>();
        var successResult = new KufarSearchResult(new List<Advertisement> { ad }, null, 1);
        var mockClient = new Mock<IKufarApiClient>();
        mockClient.Setup(c => c.SearchAsync(It.IsAny<KufarSearchCriteria>(), It.IsAny<CancellationToken>()))
            .Returns((KufarSearchCriteria c, CancellationToken _) =>
            {
                calls.Add(c);
                if (calls.Count == 1)
                {
                    throw new InvalidOperationException("HTTP 422");
                }

                return Task.FromResult(successResult);
            });

        var worker = new FeedPollingWorker(Mock.Of<IServiceProvider>(), NullLogger<FeedPollingWorker>.Instance);
        var feed = await dbContext.PollingFeeds.FirstAsync();
        await worker.PollSingleFeedAsync(dbContext, mockClient.Object, feed, CancellationToken.None);

        // first call carries the URL filter of the feed, the fallback retry goes without them
        Assert.Equal(1, calls[0].UrlFilters!.Count);
        Assert.Null(calls[1].UrlFilters);

        // the ad was still delivered and client-matched
        var outbox = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(outbox);
        Assert.Equal(101L, outbox[0].AdId);
    }

    [Fact]
    public async Task SearchSubscriptionService_MovesSearchToFilterSpecificFeed_AndCleansUpOldOne()
    {
        await using var dbContext = CreateInMemoryDbContext();
        dbContext.Users.Add(new UserEntity { Id = 1, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var service = new SearchSubscriptionService(dbContext, NullLogger<SearchSubscriptionService>.Instance);

        string filtersJson = SavedFilter.Serialize(new[]
        {
            new SavedFilter { Kind = SavedFilterKind.Multi, Key = "computers_component_type", UrlName = "cct", Title = "Тип", Values = new[] { "11", "5" } }
        });
        var search = await service.SaveSearchAsync(1, 16010, "Комплектующие", null, null, null, null, filtersJson);

        var filterFeed = await dbContext.PollingFeeds.SingleAsync(f => f.SubcategoryId == 16010);
        Assert.Equal(UrlFiltersBuilder.CanonicalJson(new Dictionary<string, string> { ["cct"] = "v.or:11,5" }), filterFeed.UrlFiltersJson);

        // user resets filters: the search must move to the legacy "{}" feed and the old one is dropped
        Assert.True(await service.UpdateSearchFiltersAsync(search.Id, 1, "[]"));

        var feeds = await dbContext.PollingFeeds.ToListAsync();
        var legacyFeed = Assert.Single(feeds, f => f.UrlFiltersJson == "{}");
        Assert.Equal(16010, legacyFeed.SubcategoryId);
    }
}
