using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.App.Telegram;
using KufarBot.Core.Analytics;
using KufarBot.Core.Http;
using KufarBot.Infrastructure.Analytics;
using KufarBot.Infrastructure.Persistence;
using KufarBot.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace KufarBot.Tests;

/// <summary>eph-17: usage analytics — event recording, metrics, periods (UTC), admin access.</summary>
public class UsageAnalyticsTests
{
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

    private static UsageEventEntity Event(
        long userId, string type, string? propertiesJson = null, DateTime? atUtc = null)
    {
        return new UsageEventEntity
        {
            UserId = userId,
            EventType = type,
            OccurredAtUtc = atUtc ?? DateTime.UtcNow,
            PropertiesJson = propertiesJson
        };
    }

    private static async Task SeedSubscriptionAsync(AppDbContext dbContext, long userId)
    {
        dbContext.UserSearches.Add(new UserSearchEntity
        {
            UserId = userId,
            SubcategoryId = 16010,
            SubcategoryTitle = "Комплектующие",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    // ---------- (a) event recording ----------

    [Fact]
    public async Task RecordAsync_PersistsEventRow()
    {
        await using var dbContext = CreateContext();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);

        await analytics.RecordAsync(501, UsageEventTypes.BotStarted, ct: CancellationToken.None);
        await analytics.RecordAsync(501, UsageEventTypes.SearchCreated,
            new { target = "search:7", preset = "Комплектующие" }, CancellationToken.None);

        var rows = await dbContext.UsageEvents.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(UsageEventTypes.BotStarted, rows[0].EventType);
        Assert.Equal(501, rows[0].UserId);
        Assert.Equal(UsageEventTypes.SearchCreated, rows[1].EventType);
        Assert.Equal("search:7", JsonDocument.Parse(rows[1].PropertiesJson!).RootElement.GetProperty("target").GetString());
        Assert.NotEqual(DateTime.MinValue, rows[0].OccurredAtUtc);
    }

    // ---------- (b) analytics failure must not break the subscription flow ----------

    [Fact]
    public async Task SearchSubscriptionService_AppliesWhenAnalyticsThrows()
    {
        await using var dbContext = CreateContext();
        dbContext.Users.Add(new UserEntity { Id = 601, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var throwing = new Mock<IUsageAnalytics>();
        throwing.Setup(a => a.RecordAsync(
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("analytics is down"));

        var service = new SearchSubscriptionService(
            dbContext, NullLogger<SearchSubscriptionService>.Instance, throwing.Object);

        var search = await service.SaveSearchAsync(601, 16010, "Комплектующие", null, null, null, null);

        // search landed even though analytics blew up
        var saved = await dbContext.UserSearches.FirstOrDefaultAsync(s => s.Id == search.Id);
        Assert.NotNull(saved);
        Assert.Empty(await dbContext.UsageEvents.ToListAsync());
    }

    [Fact]
    public async Task SearchSubscriptionService_CreatesSearchRecordsEvent()
    {
        await using var dbContext = CreateContext();
        dbContext.Users.Add(new UserEntity { Id = 602, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);
        var service = new SearchSubscriptionService(
            dbContext, NullLogger<SearchSubscriptionService>.Instance, analytics);

        var search = await service.SaveSearchAsync(602, 16010, "Комплектующие", null, null, null, null);

        var ev = await dbContext.UsageEvents.SingleAsync();
        Assert.Equal(UsageEventTypes.SearchCreated, ev.EventType);
        Assert.Equal(602, ev.UserId);
        // Cyrillic in PropertiesJson is escaped as \uXXXX (default JsonEncoder) — parse it instead
        Assert.Equal("Комплектующие",
            JsonDocument.Parse(ev.PropertiesJson!).RootElement.GetProperty("preset").GetString());
    }

    // ---------- (c) top presets / features grouping ----------

    [Fact]
    public async Task GetStatsAsync_GroupsTopPresetsAndFeatures()
    {
        await using var dbContext = CreateContext();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);
        DateTime now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        dbContext.UsageEvents.AddRange(
            Event(1, UsageEventTypes.PresetSelected, """{"preset":"Состояние = 1","target":"cnd"}""", now.AddHours(-2)),
            Event(2, UsageEventTypes.PresetSelected, """{"preset":"Состояние = 1","target":"cnd"}""", now.AddHours(-3)),
            Event(3, UsageEventTypes.PresetSelected, """{"preset":"Благоустройство = 2","target":"bld"}""", now.AddHours(-4)),
            Event(1, UsageEventTypes.FeatureUsed, """{"feature":"favorite","target":"ad:5"}""", now.AddHours(-2)),
            Event(2, UsageEventTypes.FeatureUsed, """{"feature":"favorite","target":"ad:6"}""", now.AddHours(-3)),
            Event(3, UsageEventTypes.FeatureUsed, """{"feature":"favorite","target":"ad:7"}""", now.AddHours(-4)),
            Event(1, UsageEventTypes.FeatureUsed, """{"feature":"preview","target":"sub:16"}""", now.AddHours(-2)));
        await dbContext.SaveChangesAsync();

        var report = await analytics.GetStatsAsync(AnalyticsPeriod.Day, CancellationToken.None, now);

        Assert.Equal(2, report.TopPresets.Count);
        Assert.Equal("Состояние = 1", report.TopPresets[0].Key);
        Assert.Equal(2, report.TopPresets[0].Count);

        Assert.Equal(2, report.TopFeatures.Count);
        Assert.Equal("favorite", report.TopFeatures[0].Key);
        Assert.Equal(3, report.TopFeatures[0].Count);
        Assert.Equal("preview", report.TopFeatures[1].Key);
    }

    // ---------- (d) UTC period boundaries ----------

    [Fact]
    public async Task GetStatsAsync_DayBoundary_EventExactlyAtStart_IsIncluded()
    {
        await using var dbContext = CreateContext();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);
        DateTime now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        dbContext.UsageEvents.Add(Event(11, UsageEventTypes.BotStarted, atUtc: now.AddHours(-24)));
        await dbContext.SaveChangesAsync();

        var report = await analytics.GetStatsAsync(AnalyticsPeriod.Day, CancellationToken.None, now);
        Assert.Equal(1, report.ActiveUsers1d);
    }

    [Fact]
    public async Task GetStatsAsync_DayBoundary_EventJustBeforeStart_IsExcluded()
    {
        await using var dbContext = CreateContext();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);
        DateTime now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        dbContext.UsageEvents.Add(Event(12, UsageEventTypes.BotStarted, atUtc: now.AddHours(-24).AddMinutes(-1)));
        await dbContext.SaveChangesAsync();

        var report = await analytics.GetStatsAsync(AnalyticsPeriod.Day, CancellationToken.None, now);
        Assert.Equal(0, report.ActiveUsers1d);
    }

    // ---------- (e) unique users ----------

    [Fact]
    public async Task GetStatsAsync_ActiveUsersCountsDistinctUsers()
    {
        await using var dbContext = CreateContext();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);
        DateTime now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        // same user twice within 24h + an old event outside the window
        dbContext.UsageEvents.AddRange(
            Event(21, UsageEventTypes.BotStarted, atUtc: now.AddHours(-2)),
            Event(21, UsageEventTypes.FeatureUsed, """{"feature":"preview"}""", now.AddHours(-1)),
            Event(22, UsageEventTypes.BotStarted, atUtc: now.AddDays(-2))); // only in the 7d/30d windows
        await dbContext.SaveChangesAsync();

        var report = await analytics.GetStatsAsync(AnalyticsPeriod.Day, CancellationToken.None, now);
        Assert.Equal(1, report.ActiveUsers1d);
        Assert.Equal(2, report.ActiveUsers7d);
        Assert.Equal(2, report.ActiveUsers30d);
    }

    // ---------- (f) conversion: /start -> first search ----------

    [Fact]
    public async Task GetStatsAsync_ConversionToFirstSearch()
    {
        await using var dbContext = CreateContext();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);
        DateTime now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        dbContext.UsageEvents.AddRange(
            Event(31, UsageEventTypes.BotStarted, atUtc: now.AddHours(-3)),
            Event(32, UsageEventTypes.BotStarted, atUtc: now.AddHours(-3)),
            Event(31, UsageEventTypes.SearchCreated, """{"target":"search:1"}""", now.AddHours(-2)));
        await dbContext.SaveChangesAsync();

        var report = await analytics.GetStatsAsync(AnalyticsPeriod.Day, CancellationToken.None, now);

        Assert.Equal(2, report.BotStartsInPeriod);
        Assert.Equal(1, report.ConvertedToFirstSearch);
        Assert.Equal(50.0, report.ConversionPercent!.Value);
    }

    [Fact]
    public async Task GetStatsAsync_NoStartsInPeriod_ConversionIsNull()
    {
        await using var dbContext = CreateContext();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);
        DateTime now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        var report = await analytics.GetStatsAsync(AnalyticsPeriod.Day, CancellationToken.None, now);
        Assert.Null(report.ConversionPercent);
    }

    // ---------- notifications + limit share ----------

    [Fact]
    public async Task GetStatsAsync_NotificationsAndLimitShare()
    {
        await using var dbContext = CreateContext();
        var analytics = new UsageAnalytics(dbContext, NullLogger<UsageAnalytics>.Instance);
        DateTime now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        dbContext.Users.AddRange(
            new UserEntity { Id = 41, CreatedAtUtc = DateTime.UtcNow },
            new UserEntity { Id = 42, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        dbContext.UsageEvents.AddRange(
            Event(41, UsageEventTypes.ListingNotificationSent, """{"target":"ad:1,search:1"}""", now.AddHours(-1)),
            Event(41, UsageEventTypes.ListingNotificationSent, """{"target":"ad:2,search:1"}""", now.AddHours(-2)),
            // delivered outside the window — not counted
            Event(41, UsageEventTypes.ListingNotificationSent, """{"target":"ad:3,search:1"}""", now.AddDays(-2)),
            // 2 active users, 1 hit the limit
            Event(41, UsageEventTypes.LimitReached, """{"limit":10,"active":10}""", now.AddHours(-1)),
            Event(41, UsageEventTypes.BotStarted, atUtc: now.AddHours(-1)),
            Event(42, UsageEventTypes.BotStarted, atUtc: now.AddHours(-1)));
        await dbContext.SaveChangesAsync();
        await SeedSubscriptionAsync(dbContext, 41);

        var report = await analytics.GetStatsAsync(AnalyticsPeriod.Day, CancellationToken.None, now);

        Assert.Equal(2, report.NotificationsSent);
        Assert.Equal(1, report.ActiveSearches);
        Assert.Equal(50.0, report.LimitSharePercent!.Value); // 2 active users, 1 hit the limit
    }

    // ---------- (g) /admin access ----------

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<BotEngine>
    {
        public System.Collections.Generic.List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Entries.Add($"{logLevel}: {formatter(state, exception)} {exception}");
            }
        }
    }

    private static Mock<ITelegramBotClient> BuildBotMock() => new(MockBehavior.Loose);

    private static AppDbContext AttachedContext(IServiceCollection services, AppDbContext dbContext)
    {
        services.AddSingleton(dbContext);
        services.AddSingleton<ISearchSubscriptionService>(sp => new SearchSubscriptionService(
            dbContext, NullLogger<SearchSubscriptionService>.Instance));
        services.AddSingleton(Mock.Of<ICategoryService>());
        services.AddSingleton(Mock.Of<IKufarApiClient>());
        services.AddSingleton<IUsageAnalytics>(sp => new UsageAnalytics(
            dbContext, NullLogger<UsageAnalytics>.Instance));
        return dbContext;
    }

    private static string[] SentTexts(Mock<ITelegramBotClient> botMock) => botMock.Invocations
        .Where(i => i.Method.Name == "SendRequest")
        .Select(i => i.Arguments.FirstOrDefault())
        .Select(a => a?.GetType().GetProperty("Text")?.GetValue(a) as string)
        .Where(t => !string.IsNullOrEmpty(t))
        .ToArray()!;

    [Fact]
    public async Task AdminStatsCallback_NonAdmin_GetsDenyMessageOnly()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        AttachedContext(services, dbContext);

        var botMock = BuildBotMock();
        var logger = new CapturingLogger();
        var engine = new BotEngine(
            services.BuildServiceProvider(),
            botMock.Object,
            new UserSessionStore(),
            logger,
            adminOptions: new AdminOptions()); // no admins configured

        var update = new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "q1",
                Data = "admst_d",
                From = new User { Id = 99901, Username = "notadmin" },
                Message = new Message
                {
                    Id = 10,
                    Chat = new Chat { Id = 99901, Type = ChatType.Private }
                }
            }
        };

        await engine.HandleUpdateAsync(botMock.Object, update, CancellationToken.None);

        var texts = SentTexts(botMock);
        Assert.Single(texts);
        Assert.Contains("только администратору", texts[0]);
        Assert.DoesNotContain("Аналитика использования", texts[0]);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AdminStatsCallback_Admin_GetsStatsReport()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        AttachedContext(services, dbContext);

        var botMock = BuildBotMock();
        var logger = new CapturingLogger();
        var engine = new BotEngine(
            services.BuildServiceProvider(),
            botMock.Object,
            new UserSessionStore(),
            logger,
            adminOptions: new AdminOptions { UserIds = new long[] { 55501 } });

        var update = new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = "q2",
                Data = "admst_w",
                From = new User { Id = 55501, Username = "admin" },
                Message = new Message
                {
                    Id = 11,
                    Chat = new Chat { Id = 55501, Type = ChatType.Private }
                }
            }
        };

        await engine.HandleUpdateAsync(botMock.Object, update, CancellationToken.None);

        var texts = SentTexts(botMock);
        Assert.Single(texts);
        Assert.Contains("Аналитика использования", texts[0]);
        Assert.Contains("неделя", texts[0]);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AdminStatsCommand_NonAdmin_GetsDenyMessageOnly()
    {
        await using var dbContext = CreateContext();
        var services = new ServiceCollection();
        AttachedContext(services, dbContext);

        var botMock = BuildBotMock();
        var engine = new BotEngine(
            services.BuildServiceProvider(),
            botMock.Object,
            new UserSessionStore(),
            NullLogger<BotEngine>.Instance,
            adminOptions: new AdminOptions());

        var update = new Update
        {
            Message = new Message
            {
                Id = 1,
                Date = DateTime.UtcNow,
                Chat = new Chat { Id = 88801, Type = ChatType.Private },
                From = new User { Id = 88801, Username = "user" },
                Text = "/admin"
            }
        };

        await engine.HandleUpdateAsync(botMock.Object, update, CancellationToken.None);

        var texts = SentTexts(botMock);
        Assert.Single(texts);
        Assert.Contains("только администратору", texts[0]);
        Assert.DoesNotContain("Аналитика использования", texts[0]);
    }
}
