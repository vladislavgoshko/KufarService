using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.App.Telegram;
using KufarBot.Core.Analytics;
using KufarBot.Core.Filters;
using KufarBot.Core.Http;
using KufarBot.Infrastructure.Analytics;
using KufarBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace KufarBot.Tests;

/// <summary>eph-18: ready-made search presets — JSON catalog loading and bot flows.</summary>
public class SearchPresetsTests
{
    // ===================== catalog loading =====================

    private static string Temp(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"presets-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string CatalogJson = """
        [
          {
            "id": "rent-apartments",
            "name": "Аренда квартир",
            "description": "Долгосрочная аренда квартир.",
            "category": "Недвижимость",
            "subcategoryId": 1010,
            "subcategoryTitle": "Квартиры",
            "urlFilters": [ { "key": "typ", "value": "let", "title": "Тип сделки: аренда" } ],
            "sortOrder": 1
          },
          {
            "id": "iphone",
            "name": "iPhone",
            "description": "Телефоны iPhone.",
            "category": "Смартфоны",
            "subcategoryId": 17010,
            "subcategoryTitle": "Мобильные телефоны",
            "queryWords": [ "iphone" ],
            "sortOrder": 2
          },
          {
            "id": "@(bad_id!)",
            "name": "Не пройдёт",
            "subcategoryId": 16040,
            "sortOrder": 3
          },
          {
            "id": "inactive",
            "name": "Скрытый пресет",
            "subcategoryId": 16040,
            "isActive": false,
            "sortOrder": 4
          }
        ]
        """;

    [Fact]
    public void Catalog_LoadsPresets_AndBuildsFilters()
    {
        var catalog = new SearchPresetCatalog(Temp(CatalogJson));

        // invalid id / inactive presets are skipped
        Assert.Equal(2, catalog.Presets.Count);
        Assert.Equal(new[] { "Недвижимость", "Смартфоны" }, catalog.Categories.ToArray());

        var rent = catalog.FindById("rent-apartments")!;
        Assert.Equal("Аренда квартир", rent.Name);
        Assert.Equal(1010, rent.SubcategoryId);
        var urlFilter = rent.Filters.Single(f => f.Kind == SavedFilterKind.Single);
        Assert.Equal("typ", urlFilter.UrlName);
        Assert.Equal(new[] { "let" }, urlFilter.Values);

        var iphone = catalog.FindById("iphone")!;
        var queryFilter = iphone.Filters.Single(f => f.Kind == SavedFilterKind.Query);
        Assert.Equal("own_query", queryFilter.Key);
        Assert.Equal(new[] { "iphone" }, queryFilter.Values);
        Assert.Null(iphone.CityId);
        Assert.Null(iphone.MinPriceByn);
    }

    [Fact]
    public void Catalog_MapsQueryKeywordsAndSeller()
    {
        string json = """
            [
              {
                "id": "mixed",
                "name": "Смешанный",
                "subcategoryId": 17010,
                "queryWords": [ "samsung galaxy", "SAMSUNG" ],
                "keywordsExclude": [ " case ", "" ],
                "sellerType": "PRIVATE"
              }
            ]
            """;
        var catalog = new SearchPresetCatalog(Temp(json));
        var preset = catalog.FindById("mixed")!;

        var query = preset.Filters.Single(f => f.Kind == SavedFilterKind.Query);
        // words are split on spaces, deduplicated case-insensitively
        Assert.Equal(new[] { "samsung", "galaxy" }, query.Values);

        var exclude = preset.Filters.Single(f => f.Kind == SavedFilterKind.KeywordsExclude);
        Assert.Equal(new[] { "case" }, exclude.Values);

        var seller = preset.Filters.Single(f => f.Kind == SavedFilterKind.SellerType);
        Assert.Equal(new[] { "private" }, seller.Values);
    }

    [Fact]
    public void Catalog_MissingOrBrokenFile_YieldsEmptyCatalog()
    {
        Assert.Empty(new SearchPresetCatalog(Path.Combine(Path.GetTempPath(), "missing-presets.json")).Presets);
        Assert.Empty(new SearchPresetCatalog(Temp("{ not json ]")).Presets);
    }

    // ===================== bot engine flows =====================

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var context = new AppDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static ISearchPresetCatalog TestCatalog()
    {
        return new SearchPresetCatalog(Temp(CatalogJson));
    }

    private static BotEngine BuildEngine(IServiceCollection services, AppDbContext dbContext,
        Mock<ITelegramBotClient> botMock, ISearchPresetCatalog catalog)
    {
        services.AddSingleton(dbContext);
        services.AddSingleton<ISearchSubscriptionService>(sp => new SearchSubscriptionService(
            dbContext, NullLogger<SearchSubscriptionService>.Instance));
        services.AddSingleton(Mock.Of<ICategoryService>());
        services.AddSingleton(Mock.Of<IKufarApiClient>());
        services.AddSingleton<IUsageAnalytics>(sp => new UsageAnalytics(
            dbContext, NullLogger<UsageAnalytics>.Instance));

        return new BotEngine(
            services.BuildServiceProvider(),
            botMock.Object,
            new UserSessionStore(),
            NullLogger<BotEngine>.Instance,
            presetCatalog: catalog);
    }

    private static Update Callback(long userId, string data) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            Id = "q",
            Data = data,
            From = new User { Id = userId },
            Message = new Message { Id = 1, Chat = new Chat { Id = userId, Type = ChatType.Private } }
        }
    };

    private static string[] SentTexts(Mock<ITelegramBotClient> botMock) => botMock.Invocations
        .Where(i => i.Method.Name == "SendRequest")
        .Select(i => i.Arguments.FirstOrDefault())
        .Select(a => a?.GetType().GetProperty("Text")?.GetValue(a) as string)
        .Where(t => !string.IsNullOrEmpty(t))
        .ToArray()!;

    [Fact]
    public async Task MenuReady_ShowsCatalog_AndRecordsView()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        await engine.HandleUpdateAsync(botMock.Object, Callback(7001, "menu_ready"), CancellationToken.None);

        Assert.Contains("Готовые поиски", SentTexts(botMock)[0]);
        Assert.Contains("Выберите категорию", SentTexts(botMock)[0]);

        var viewEvent = await dbContext.UsageEvents.SingleAsync(e => e.EventType == UsageEventTypes.FeatureUsed);
        Assert.Equal("preset_catalog_view",
            JsonDocument.Parse(viewEvent.PropertiesJson!).RootElement.GetProperty("feature").GetString());
    }

    [Fact]
    public async Task PresetView_RecordsSelection_WithSearchpresetTarget()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        await engine.HandleUpdateAsync(botMock.Object, Callback(7002, "preset_v_iphone"), CancellationToken.None);

        Assert.Contains("iPhone", SentTexts(botMock)[0]);
        var ev = await dbContext.UsageEvents.SingleAsync(e => e.EventType == UsageEventTypes.PresetSelected);
        var props = JsonDocument.Parse(ev.PropertiesJson!).RootElement;
        Assert.Equal("searchpreset:iphone", props.GetProperty("target").GetString());
        Assert.Equal("iPhone", props.GetProperty("preset").GetString());
        Assert.Equal("Смартфоны", props.GetProperty("category").GetString());
    }

    [Fact]
    public async Task PresetCreate_CreatesSearch_AndRecordsPresetCreated()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        await engine.HandleUpdateAsync(botMock.Object, Callback(7003, "preset_create_iphone"), CancellationToken.None);

        var search = await dbContext.UserSearches.SingleAsync();
        Assert.Equal(17010, search.SubcategoryId);
        Assert.Equal("own_query", SavedFilter.Deserialize(search.FiltersJson).Single().Key);
        Assert.Contains("успешно сохранена", SentTexts(botMock)[0]);

        var created = await dbContext.UsageEvents.SingleAsync(e => e.EventType == UsageEventTypes.FeatureUsed);
        Assert.Equal("preset_created",
            JsonDocument.Parse(created.PropertiesJson!).RootElement.GetProperty("feature").GetString());
        Assert.Contains("searchpreset:iphone",
            JsonDocument.Parse(created.PropertiesJson!).RootElement.GetProperty("target").GetString());
    }

    [Fact]
    public async Task PresetCreate_AtLimit_BlocksNewSearch_AndRecordsLimit()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        dbContext.Users.Add(new UserEntity { Id = 7004, CreatedAtUtc = DateTime.UtcNow });
        for (int i = 0; i < 10; i++)
        {
            dbContext.UserSearches.Add(new UserSearchEntity
            {
                UserId = 7004,
                SubcategoryId = 16010 + i,
                SubcategoryTitle = $"Cat {i}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        await dbContext.SaveChangesAsync();

        await engine.HandleUpdateAsync(botMock.Object, Callback(7004, "preset_create_iphone"), CancellationToken.None);

        Assert.Equal(10, await dbContext.UserSearches.CountAsync());
        Assert.Contains("Достигнут лимит", SentTexts(botMock)[0]);
        Assert.True(await dbContext.UsageEvents.AnyAsync(e => e.EventType == UsageEventTypes.LimitReached));
        // no preset_created attribution for a blocked creation
        Assert.Empty(await dbContext.UsageEvents.Where(
            e => e.EventType == UsageEventTypes.FeatureUsed &&
                 e.PropertiesJson!.Contains("preset_created")).ToListAsync());
    }

    [Fact]
    public async Task PresetCancel_RecordsDecline()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        await engine.HandleUpdateAsync(botMock.Object, Callback(7005, "preset_cancel_iphone"), CancellationToken.None);

        Assert.Contains("каталог готовых поисков", SentTexts(botMock)[0]);
        var ev = await dbContext.UsageEvents.SingleAsync(e => e.EventType == UsageEventTypes.PresetDeclined);
        Assert.Equal("searchpreset:iphone",
            JsonDocument.Parse(ev.PropertiesJson!).RootElement.GetProperty("target").GetString());
    }

    [Fact]
    public async Task PresetCfg_BuildsDraft_WithPresetFilters()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        await engine.HandleUpdateAsync(botMock.Object, Callback(7006, "preset_cfg_iphone"), CancellationToken.None);

        Assert.Contains("Пресет", SentTexts(botMock)[0]);
        // preset flow keeps going through the ordinary draft -> draft_save, which records
        // preset_created attribution from the draft's PresetId; catalogue stays unchanged
        Assert.Empty(await dbContext.UserTemplates.ToListAsync());
    }

    [Fact]
    public async Task TplFrom_Search_BecomesPersonalTemplate_WithUniqueName()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());
        var service = services.BuildServiceProvider().GetRequiredService<ISearchSubscriptionService>();

        dbContext.Users.Add(new UserEntity { Id = 7007, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();
        var search = await service.SaveSearchAsync(7007, 16010, "Комплектующие", null, null, null, null);

        await engine.HandleUpdateAsync(botMock.Object, Callback(7007, $"tpl_from_{search.Id}"), CancellationToken.None);
        await engine.HandleUpdateAsync(botMock.Object, Callback(7007, $"tpl_from_{search.Id}"), CancellationToken.None);

        var templates = await dbContext.UserTemplates.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, templates.Count);
        Assert.Equal("Комплектующие", templates[0].Name);
        Assert.Equal("Комплектующие 2", templates[1].Name); // name collision resolved

        // both saves are attributed to the source subscription
        var tplEvents = await dbContext.UsageEvents
            .Where(e => e.EventType == UsageEventTypes.FeatureUsed)
            .ToListAsync();
        Assert.Equal(2, tplEvents.Count);
        Assert.Contains("search:",
            JsonDocument.Parse(tplEvents[0].PropertiesJson!).RootElement.GetProperty("target").GetString());
        Assert.Contains("search:",
            JsonDocument.Parse(tplEvents[1].PropertiesJson!).RootElement.GetProperty("target").GetString());
    }

    [Fact]
    public async Task TplFromPreset_KeepsCatalogAndTemplateSeparated()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        await engine.HandleUpdateAsync(botMock.Object, Callback(7008, "tpl_frompreset_iphone"), CancellationToken.None);

        var template = await dbContext.UserTemplates.SingleAsync();
        Assert.Equal("iPhone", template.Name);
        Assert.Equal(17010, template.SubcategoryId);
        Assert.Contains("own_query", template.FiltersJson);
    }

    [Fact]
    public async Task TemplateFullCycle_CreateRenameDelete()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        await engine.HandleUpdateAsync(botMock.Object, Callback(7009, "tpl_frompreset_iphone"), CancellationToken.None);
        await engine.HandleUpdateAsync(botMock.Object, Callback(7009, "tpl_frompreset_rent-apartments"), CancellationToken.None);
        var templates = await dbContext.UserTemplates.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, templates.Count);
        int templateId = templates[0].Id; // iPhone

        // create a search from the template
        await engine.HandleUpdateAsync(botMock.Object, Callback(7009, $"tpl_create_{templateId}"), CancellationToken.None);
        Assert.Equal(1, await dbContext.UserSearches.CountAsync());
        Assert.True(SentTexts(botMock).Any(t => t.Contains("шаблону")), "search-from-template confirmation expected");

        // rename: first try the name of the OTHER template -> conflict re-arms pending input
        await engine.HandleUpdateAsync(botMock.Object, Callback(7009, $"tpl_rename_{templateId}"), CancellationToken.None);
        await engine.HandleUpdateAsync(botMock.Object, new Update
        {
            Message = new Message
            {
                Id = 2, Date = DateTime.UtcNow,
                Chat = new Chat { Id = 7009, Type = ChatType.Private },
                From = new User { Id = 7009 }, Text = "Аренда квартир"
            }
        }, CancellationToken.None);
        Assert.True(SentTexts(botMock).Any(t => t.Contains("уже есть")), "name-conflict message expected");
        Assert.Equal(2, templates.Count);

        // retry with a fresh name — rename lands, nothing is duplicated
        await engine.HandleUpdateAsync(botMock.Object, new Update
        {
            Message = new Message
            {
                Id = 3, Date = DateTime.UtcNow,
                Chat = new Chat { Id = 7009, Type = ChatType.Private },
                From = new User { Id = 7009 }, Text = "Айфоны"
            }
        }, CancellationToken.None);
        templates = await dbContext.UserTemplates.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal("Айфоны", templates.Single(t => t.Id == templateId).Name);

        // delete with confirmation
        await engine.HandleUpdateAsync(botMock.Object, Callback(7009, $"tpl_delconf_{templateId}"), CancellationToken.None);
        Assert.True(SentTexts(botMock).Any(t => t.Contains("Удалить шаблон")), "delete confirmation expected");
        await engine.HandleUpdateAsync(botMock.Object, Callback(7009, $"tpl_del_{templateId}"), CancellationToken.None);
        Assert.Single(await dbContext.UserTemplates.ToListAsync()); // the other template survives
        // subscription created from the template survives the template deletion
        Assert.Equal(1, await dbContext.UserSearches.CountAsync());
    }

    [Fact]
    public async Task RenameCommand_DoesNotConsumePendingState()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        var botMock = new Mock<ITelegramBotClient>(MockBehavior.Loose);
        var engine = BuildEngine(services, dbContext, botMock, TestCatalog());

        await engine.HandleUpdateAsync(botMock.Object, Callback(7010, "tpl_frompreset_iphone"), CancellationToken.None);
        int templateId = (await dbContext.UserTemplates.SingleAsync()).Id;
        await engine.HandleUpdateAsync(botMock.Object, Callback(7010, $"tpl_rename_{templateId}"), CancellationToken.None);

        // slash-commands pass through instead of being treated as a template name
        await engine.HandleUpdateAsync(botMock.Object, new Update
        {
            Message = new Message
            {
                Id = 2, Date = DateTime.UtcNow,
                Chat = new Chat { Id = 7010, Type = ChatType.Private },
                From = new User { Id = 7010 }, Text = "/help"
            }
        }, CancellationToken.None);

        Assert.True(SentTexts(botMock).Any(t => t.Contains("справка")), "help text expected");
        Assert.Equal("iPhone", (await dbContext.UserTemplates.SingleAsync()).Name); // untouched
    }
}
