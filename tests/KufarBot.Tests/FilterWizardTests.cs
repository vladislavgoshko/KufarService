using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.App.Telegram;
using KufarBot.Core.Filters;
using KufarBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KufarBot.Tests;

public class FilterWizardTests
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

    // ---------------- Range input parsing ----------------

    [Theory]
    [InlineData("8 32", "8", "32")]
    [InlineData("8-32", "8", "32")]
    [InlineData("8 – 32", "8", "32")]
    [InlineData("от 8", "8", "")]
    [InlineData("до 32", "", "32")]
    [InlineData("8+", "8", "")]
    [InlineData("8", "8", "")]
    [InlineData("8,32", "8", "32")]
    public void ParseRangeInput_ValidFormats(string input, string expectedMin, string expectedMax)
    {
        var result = FilterWizardUI.ParseRangeInput(input);

        Assert.True(result.Success, result.Error);
        Assert.Equal(string.IsNullOrEmpty(expectedMin) ? (decimal?)null : decimal.Parse(expectedMin), result.Min);
        Assert.Equal(string.IsNullOrEmpty(expectedMax) ? (decimal?)null : decimal.Parse(expectedMax), result.Max);
    }

    [Theory]
    [InlineData("абракадабра")]
    [InlineData("32 8")]      // min > max
    [InlineData("8 16 32")]   // more than two numbers
    [InlineData("")]
    public void ParseRangeInput_InvalidFormats(string input)
    {
        Assert.False(FilterWizardUI.ParseRangeInput(input).Success);
    }

    [Fact]
    public void ParseKeywordsInput_SplitsAndDeduplicates()
    {
        var keywords = FilterWizardUI.ParseKeywordsInput("gtx, 1060; обмен\nGTX");

        Assert.Equal(new[] { "gtx", "1060", "обмен" }, keywords);
        Assert.Empty(FilterWizardUI.ParseKeywordsInput("   "));
    }

    // ---------------- Keyboards / spec helpers ----------------

    [Fact]
    public void FiltersMenu_RendersWithoutSpecsAndWithOwnFilters()
    {
        var draft = new UserSearchDraft
        {
            SubcategoryId = 16010,
            Wizard = new FilterWizardState(),
            Filters =
            {
                new SavedFilter { Kind = SavedFilterKind.KeywordsInclude, Key = "own_kw_inc", Title = "Ключевые слова (включить)", Values = new[] { "gtx" } }
            }
        };

        var markup = BotKeyboards.FiltersMenu(draft);
        Assert.NotEqual(default, markup);
        // own filters + done + back rows are present even when no specs available
        var texts = markup.InlineKeyboard.SelectMany(r => r).Select(b => b.Text).ToList();
        Assert.Contains("🎛 Свои фильтры", texts);
        Assert.Contains("✔️ Готово", texts);
    }

    [Fact]
    public void HasValue_IsKindCompatible()
    {
        var spec = new FilterSpec { Key = "condition", Title = "Состояние", Kind = FilterKind.Single, Options = new[] { new FilterOption("1", "Новое") } };
        var filters = new System.Collections.Generic.List<SavedFilter>
        {
            new() { Kind = SavedFilterKind.Single, Key = "condition", Title = "Состояние", Values = new[] { "1" } }
        };

        Assert.True(BotKeyboards.HasValue(filters, spec));
        Assert.False(BotKeyboards.HasValue(new System.Collections.Generic.List<SavedFilter>(), spec));
    }

    // ---------------- Persistence with filters ----------------

    [Fact]
    public async Task SaveSearch_WithFilters_PersistsAndKeepsDedupUpdated()
    {
        await using var dbContext = CreateInMemoryDbContext();

        dbContext.Users.Add(new UserEntity { Id = 1, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dbContext);
        using var provider = services.BuildServiceProvider();

        var service = new SearchSubscriptionService(
            provider.GetRequiredService<AppDbContext>(),
            NullLogger<SearchSubscriptionService>.Instance);

        string filtersJson = SavedFilter.Serialize(new[]
        {
            new SavedFilter { Kind = SavedFilterKind.Single, Key = "condition", Title = "Состояние", Values = new[] { "1" } }
        });

        var created = await service.SaveSearchAsync(1, 16010, "Комплектующие", 7, "Минск", 10m, 100m, filtersJson);

        Assert.Equal(filtersJson, created.FiltersJson);

        // feed created for our exact (subcat, city) pair
        Assert.True(await dbContext.PollingFeeds.AnyAsync(f => f.SubcategoryId == 16010 && f.CityId == 7));

        // Same (subcat, city, prices) again, but new filters -> existing search gets updated
        string otherFilters = SavedFilter.Serialize(Array.Empty<SavedFilter>());
        var second = await service.SaveSearchAsync(1, 16010, "Комплектующие", 7, "Минск", 10m, 100m, otherFilters);

        Assert.Equal(created.Id, second.Id);
        Assert.Equal("[]", second.FiltersJson);
        Assert.Single(await dbContext.UserSearches.ToListAsync());
    }

    [Fact]
    public async Task UpdateSearchFilters_FiltersJsonRoundtrip()
    {
        await using var dbContext = CreateInMemoryDbContext();

        dbContext.Users.Add(new UserEntity { Id = 7, CreatedAtUtc = DateTime.UtcNow });
        dbContext.UserSearches.Add(new UserSearchEntity
        {
            UserId = 7,
            SubcategoryId = 16010,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        int searchId = await dbContext.UserSearches.Select(s => s.Id).FirstAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dbContext);
        using var provider = services.BuildServiceProvider();
        var service = new SearchSubscriptionService(
            provider.GetRequiredService<AppDbContext>(),
            NullLogger<SearchSubscriptionService>.Instance);

        string json = SavedFilter.Serialize(new[]
        {
            new SavedFilter { Kind = SavedFilterKind.Multi, Key = "remuneration_type", Title = "Тип", Values = new[] { "1", "2" } },
            new SavedFilter { Kind = SavedFilterKind.MaxAgeHours, Key = "own_age", Title = "Возраст", Min = 24m }
        });

        Assert.True(await service.UpdateSearchFiltersAsync(searchId, 7, json));

        var stored = await service.GetSearchByIdAsync(searchId, 7);
        var restored = SavedFilter.Deserialize(stored!.FiltersJson);

        Assert.Equal(2, restored.Count);
        Assert.Equal(SavedFilterKind.MaxAgeHours, restored[1].Kind);
        Assert.Equal(24m, restored[1].Min);
    }

    // ---------------- Wizard state-machine per spec kind ----------------

    private static UserSearchDraft DraftWithSpecs(params FilterSpec[] specs)
    {
        return new UserSearchDraft
        {
            SubcategoryId = 16010,
            Wizard = new FilterWizardState(),
            AvailableSpecs = specs.ToList()
        };
    }

    [Fact]
    public void FiltersMenu_MarksChosenSpecsAndKeepsPaging()
    {
        var draft = DraftWithSpecs(
            new FilterSpec { Key = "condition", Title = "Состояние", Kind = FilterKind.Single, Options = new[] { new FilterOption("1", "Новое") } },
            new FilterSpec { Key = "mileage", Title = "Пробег", Kind = FilterKind.Range, RangeLower = 0, RangeUpper = 500000 });

        var empty = BotKeyboards.FiltersMenu(draft);
        Assert.Contains(empty.InlineKeyboard.SelectMany(r => r), b => b.Text.StartsWith("▫️ Состояние"));

        draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.Single, Key = "condition", Title = "Состояние", Values = new[] { "1" } });
        var chosen = BotKeyboards.FiltersMenu(draft);
        Assert.Contains(chosen.InlineKeyboard.SelectMany(r => r), b => b.Text.StartsWith("✅ Состояние"));
        Assert.Contains(chosen.InlineKeyboard.SelectMany(r => r), b => b.Text.StartsWith("▫️ Пробег"));
    }

    [Fact]
    public void FilterOptionsMenu_Bool_TogglesThroughOptions()
    {
        var draft = DraftWithSpecs(new FilterSpec { Key = "is_new", Title = "Новое", Kind = FilterKind.Bool, Options = new[] { new FilterOption("1", "Новое") } });

        var none = BotKeyboards.FilterOptionsMenu(draft, 0);
        Assert.Contains(none.InlineKeyboard.SelectMany(r => r), b => b.CallbackData == "fsv_0_0");
        Assert.DoesNotContain(none.InlineKeyboard.SelectMany(r => r), b => b.CallbackData == "fclr_0");

        draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.Bool, Key = "is_new", Title = "Новое", Values = new[] { "1" } });
        var chosen = BotKeyboards.FilterOptionsMenu(draft, 0);
        Assert.Contains(chosen.InlineKeyboard.SelectMany(r => r), b => b.CallbackData == "fclr_0");
    }

    [Fact]
    public void FilterOptionsMenu_Multi_SecondPageHasConfirmAndClear()
    {
        var options = Enumerable.Range(1, 8).Select(i => new FilterOption(i.ToString(), $"Вариант {i}")).ToArray();
        var draft = DraftWithSpecs(new FilterSpec { Key = "remuneration", Title = "Тип", Kind = FilterKind.Multi, Options = options });

        // page 2
        draft.Wizard!.SelectedValuesPage = 1;
        var menu = BotKeyboards.FilterOptionsMenu(draft, 0);
        var buttons = menu.InlineKeyboard.SelectMany(r => r).ToList();

        Assert.Contains(buttons, b => b.CallbackData == "fmv_0_6");
        Assert.Contains(buttons, b => b.CallbackData == "fmv_0_7");
        Assert.DoesNotContain(buttons, b => b.CallbackData == "fmv_0_5");
        Assert.Contains(buttons, b => b.CallbackData == "fmdone_0");
        Assert.Contains(buttons, b => b.CallbackData == "fvp_0_0");
    }

    [Fact]
    public void FilterOptionsMenu_Single_SelectsAndClears()
    {
        var draft = DraftWithSpecs(new FilterSpec
        {
            Key = "condition", Title = "Состояние", Kind = FilterKind.Single,
            Options = new[] { new FilterOption("1", "Новое"), new FilterOption("2", "Б/у") }
        });

        draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.Single, Key = "condition", Title = "Состояние", Values = new[] { "2" } });
        var menu = BotKeyboards.FilterOptionsMenu(draft, 0);
        var buttons = menu.InlineKeyboard.SelectMany(r => r).ToList();

        Assert.Contains(buttons, b => b.CallbackData == "fsv_0_0");
        Assert.Contains(buttons, b => b.CallbackData == "fsv_0_1");
        Assert.Contains(buttons, b => b.CallbackData == "fclr_0");
    }

    [Fact]
    public void FilterOptionsMenu_RangeAndText_ShowClearWhenSet()
    {
        var draft = DraftWithSpecs(
            new FilterSpec { Key = "mileage", Title = "Пробег", Kind = FilterKind.Range, RangeLower = 0, RangeUpper = 500000 },
            new FilterSpec { Key = "seller_note", Title = "Примечание", Kind = FilterKind.Text });

        var rangeEmpty = BotKeyboards.FilterOptionsMenu(draft, 0);
        Assert.DoesNotContain(rangeEmpty.InlineKeyboard.SelectMany(r => r), b => (b.CallbackData ?? "").StartsWith("fclr_"));

        draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.Range, Key = "mileage", Title = "Пробег", Min = 1000m, Max = 50000m });
        draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.Text, Key = "seller_note", Title = "Примечание", Values = new[] { "Гарант" } });

        var rangeSet = BotKeyboards.FilterOptionsMenu(draft, 0);
        Assert.Contains(rangeSet.InlineKeyboard.SelectMany(r => r), b => b.CallbackData == "fclr_0");

        var textSet = BotKeyboards.FilterOptionsMenu(draft, 1);
        Assert.Contains(textSet.InlineKeyboard.SelectMany(r => r), b => b.CallbackData == "fclr_1");
    }

    [Fact]
    public void RenderSpecScreen_RangeShowsBoundsAndCurrent()
    {
        var draft = DraftWithSpecs(new FilterSpec { Key = "mileage", Title = "Пробег", Kind = FilterKind.Range, RangeLower = 0, RangeUpper = 500000 });
        draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.Range, Key = "mileage", Title = "Пробег", Min = 1000m, Max = 50000m });

        string screen = FilterWizardUI.RenderSpecScreen(draft, 0);
        Assert.Contains("1000–50000", screen);
        Assert.Contains("0 – 500000", screen);
    }

    [Fact]
    public void OwnFilterValueText_FormatsAllKinds()
    {
        var query = new SavedFilter { Kind = SavedFilterKind.Query, Key = "own_query", Title = "Текст поиска", Values = new[] { "iphone", "13" } };
        var seller = new SavedFilter { Kind = SavedFilterKind.SellerType, Key = "own_seller", Title = "Продавец", Values = new[] { "company" } };
        var age = new SavedFilter { Kind = SavedFilterKind.MaxAgeHours, Key = "own_age", Title = "Возраст", Min = 72m };
        var range = new SavedFilter { Kind = SavedFilterKind.Range, Key = "mileage", Title = "Пробег", Min = 1000m, Max = 50000m };

        Assert.Equal("iphone, 13", BotKeyboards.FilterValueText(query));
        Assert.Equal("только компании", BotKeyboards.FilterValueText(seller));
        Assert.Equal("за 3 дн.", BotKeyboards.FilterValueText(age));
        Assert.Equal("1000–50000", BotKeyboards.FilterValueText(range));
    }


    [Fact]
    public void ParseQueryInput_SplitsWordsAndDeduplicates()
    {
        var words = FilterWizardUI.ParseQueryInput("iphone  13, 16 pro; iphone");
        Assert.Equal(new[] { "iphone", "13", "16", "pro" }, words);
        Assert.Empty(FilterWizardUI.ParseQueryInput("  "));
        // single chars dropped
        Assert.Empty(FilterWizardUI.ParseQueryInput("a b c"));
    }

    [Fact]
    public void QueryFilter_MatchesOnlyWhenAllWordsPresent()
    {
        var search = new UserSearchEntity
        {
            Id = 1,
            UserId = 1,
            SubcategoryId = 16010,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        search.FiltersJson = SavedFilter.Serialize(new[]
        {
            new SavedFilter { Kind = SavedFilterKind.Query, Key = "own_query", Title = "Текст поиска", Values = new[] { "iphone", "13" } }
        });

        var ad = new KufarBot.Core.Models.Advertisement
        {
            Id = 700,
            Title = "iPhone 13 128GB",
            Location = "Минск",
            AdLink = "l",
            ListTimeUtc = DateTime.UtcNow,
            Parameters = new List<KufarBot.Core.Models.AdParameter>()
        };

        Assert.True(search.Matches(ad));

        var other = new KufarBot.Core.Models.Advertisement
        {
            Id = 701,
            Title = "iPhone 12 64GB",
            Location = "Минск",
            AdLink = "l2",
            ListTimeUtc = DateTime.UtcNow,
            Parameters = new List<KufarBot.Core.Models.AdParameter>()
        };
        Assert.False(search.Matches(other));
    }

    [Fact]
    public void SubscriptionsListMenu_PaginatesTenSubscriptions()
    {
        var searches = Enumerable.Range(1, 10).Select(i => new UserSearchEntity
        {
            Id = i,
            UserId = 1,
            SubcategoryId = 16010,
            SubcategoryTitle = $"Подкатегория {i}",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }).ToList();

        var page0 = BotKeyboards.SubscriptionsListMenu(searches, 0);
        var page0Buttons = page0.InlineKeyboard.SelectMany(r => r).Select(b => b.Text).ToList();
        Assert.Equal(1, page0Buttons.Count(t => t.Contains("Подкатегория 8")));
        Assert.Equal(0, page0Buttons.Count(t => t.Contains("Подкатегория 9")));
        Assert.Contains("1/2", page0Buttons);
        Assert.Contains("➡️", page0Buttons);

        var page1 = BotKeyboards.SubscriptionsListMenu(searches, 1);
        var page1Buttons = page1.InlineKeyboard.SelectMany(r => r).Select(b => b.Text).ToList();
        Assert.Contains(page1Buttons, t => t.Contains("Подкатегория 9"));
        Assert.DoesNotContain(page1Buttons, t => t.Contains("Подкатегория 1)"));
        Assert.Contains("2/2", page1Buttons);
        Assert.Contains("⬅️", page1Buttons);
    }

    // ---------------- Matching integration with own filters ----------------

    [Fact]
    public async Task FeedPollingWorker_RespectsOwnFiltersThroughFullFlow()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 2718;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        var search = new UserSearchEntity
        {
            Id = 1,
            UserId = userId,
            SubcategoryId = 16010,
            CityId = 7,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        search.FiltersJson = KufarBot.Core.Filters.SavedFilter.Serialize(new[]
        {
            new SavedFilter { Kind = SavedFilterKind.KeywordsInclude, Key = "own_kw_inc", Title = "Ключевые", Values = new[] { "gtx" } }
        });
        dbContext.UserSearches.Add(search);

        var baseTime = new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc);
        dbContext.PollingFeeds.Add(new PollingFeedEntity
        {
            SubcategoryId = 16010,
            CityId = 7,
            LastSeenAdTimeUtc = baseTime,
            LastSeenAdIdsJson = "[]"
        });
        await dbContext.SaveChangesAsync();

        var matching = new KufarBot.Core.Models.Advertisement
        {
            Id = 501, Title = "Видеокарта GTX 1060", PriceByn = 200m, Location = "Минск",
            AdLink = "l1", ListTimeUtc = baseTime.AddMinutes(1)
        };
        var notMatching = new KufarBot.Core.Models.Advertisement
        {
            Id = 502, Title = "Видеокарта Radeon", PriceByn = 200m, Location = "Минск",
            AdLink = "l2", ListTimeUtc = baseTime.AddMinutes(2)
        };

        var mockClient = new Moq.Mock<KufarBot.Core.Http.IKufarApiClient>();
        mockClient.Setup(c => c.SearchAsync(It.IsAny<KufarBot.Core.Models.KufarSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KufarBot.Core.Http.KufarSearchResult(new[] { matching, notMatching }, null, 2));

        var worker = new KufarBot.Infrastructure.Polling.FeedPollingWorker(
            Moq.Mock.Of<IServiceProvider>(),
            NullLogger<KufarBot.Infrastructure.Polling.FeedPollingWorker>.Instance);
        var feed = await dbContext.PollingFeeds.FirstAsync();
        await worker.PollSingleFeedAsync(dbContext, mockClient.Object, feed, CancellationToken.None);

        var outbox = await dbContext.NotificationOutbox.ToListAsync();
        Assert.Single(outbox);
        Assert.Equal(501L, outbox[0].AdId);
    }
}
