using System.Text.Json;
using KufarBot.Core.Analytics;
using KufarBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KufarBot.Infrastructure.Analytics;

/// <summary>eph-17: scoped implementation over AppDbContext. All recording is
/// best-effort: unexpected failures are logged and swallowed so analytics can
/// never break search creation, notifications, or callback handling.</summary>
public class UsageAnalytics : IUsageAnalytics
{
    private static readonly JsonSerializerOptions PropsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _dbContext;
    private readonly ILogger<UsageAnalytics> _logger;

    public UsageAnalytics(AppDbContext dbContext, ILogger<UsageAnalytics> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task RecordAsync(long userId, string eventType, object? properties = null, CancellationToken ct = default)
    {
        try
        {
            _dbContext.UsageEvents.Add(new UsageEventEntity
            {
                UserId = userId,
                EventType = eventType,
                OccurredAtUtc = DateTime.UtcNow,
                PropertiesJson = properties == null ? null : JsonSerializer.Serialize(properties, PropsJsonOptions)
            });
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[UsageAnalytics] Failed to record event Type={Type} User={UserId}",
                eventType, userId);
        }
    }

    public async Task<UsageReport> GetStatsAsync(AnalyticsPeriod period, CancellationToken ct = default, DateTime? nowUtc = null)
    {
        DateTime now = nowUtc ?? DateTime.UtcNow;
        DateTime periodStart = period switch
        {
            AnalyticsPeriod.Day => now.AddHours(-24),
            AnalyticsPeriod.Week => now.AddDays(-7),
            _ => now.AddDays(-30)
        };

        var report = new UsageReport { Period = period };

        // --- Users / searches from the existing tables (no duplicate accounting) ---
        report.NewUsers = await _dbContext.Users
            .CountAsync(u => u.CreatedAtUtc >= periodStart, ct);
        report.ActiveSearches = await _dbContext.UserSearches
            .CountAsync(s => !s.IsPaused, ct);

        // --- Active users: distinct user ids with any event in rolling windows ---
        report.ActiveUsers1d = await CountActiveUsersAsync(now.AddHours(-24), ct);
        report.ActiveUsers7d = await CountActiveUsersAsync(now.AddDays(-7), ct);
        report.ActiveUsers30d = await CountActiveUsersAsync(now.AddDays(-30), ct);

        // --- Delivered notifications in the window ---
        report.NotificationsSent = await _dbContext.UsageEvents
            .CountAsync(e => e.EventType == UsageEventTypes.ListingNotificationSent &&
                             e.OccurredAtUtc >= periodStart, ct);

        // --- Top presets / features: grouped over PropertiesJson (parsed in memory) ---
        var featureJsons = await _dbContext.UsageEvents
            .Where(e => e.EventType == UsageEventTypes.FeatureUsed && e.OccurredAtUtc >= periodStart)
            .OrderByDescending(e => e.Id)
            .Take(5000)
            .Select(e => e.PropertiesJson)
            .ToListAsync(ct);

        report.TopFeatures = TopItems(featureJsons.Select(j => ParseProp(j, UsageEventTypes.PropFeature)));

        // eph-18: feature tallies used by the preset funnel section
        var featureCounts = featureJsons
            .Select(j => ParseProp(j, UsageEventTypes.PropFeature))
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        report.PresetCatalogViews = featureCounts.GetValueOrDefault(UsageFeatures.PresetCatalogView);
        report.PresetSearchesCreated = featureCounts.GetValueOrDefault(UsageFeatures.PresetCreated);

        var selectionJsons = await _dbContext.UsageEvents
            .Where(e => e.EventType == UsageEventTypes.PresetSelected && e.OccurredAtUtc >= periodStart)
            .OrderByDescending(e => e.Id)
            .Take(5000)
            .Select(e => e.PropertiesJson)
            .ToListAsync(ct);

        // "searchpreset:" targets are ready-search presets; plain target values are
        // site-filter presets chosen in the wizard.
        var siteFilterPresets = new List<string>();
        var readyPresetSelections = new List<(string Preset, string? Category)>();
        foreach (var json in selectionJsons)
        {
            var target = ParseProp(json, UsageEventTypes.PropTarget);
            var presetName = ParseProp(json, UsageEventTypes.PropPreset);
            if (target is null || !target.StartsWith("searchpreset:", StringComparison.Ordinal))
            {
                if (presetName != null)
                {
                    siteFilterPresets.Add(presetName);
                }

                continue;
            }

            report.PresetSelections++;
            if (presetName != null)
            {
                readyPresetSelections.Add((presetName, ParseProp(json, "category")));
            }
        }

        report.TopPresets = TopItems(siteFilterPresets);
        report.TopSearchPresets = TopItems(readyPresetSelections.Select(s => s.Preset));
        report.TopPresetCategories = TopItems(
            readyPresetSelections.Where(s => !string.IsNullOrEmpty(s.Category)).Select(s => s.Category!));

        report.PresetDeclines = await _dbContext.UsageEvents
            .CountAsync(e => e.EventType == UsageEventTypes.PresetDeclined &&
                             e.OccurredAtUtc >= periodStart, ct);

        // --- Limit share over active users in the window ---
        int activeUsers = period == AnalyticsPeriod.Day ? report.ActiveUsers1d
            : period == AnalyticsPeriod.Week ? report.ActiveUsers7d
            : report.ActiveUsers30d;
        int limitUsers = await _dbContext.UsageEvents
            .Where(e => e.EventType == UsageEventTypes.LimitReached && e.OccurredAtUtc >= periodStart)
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync(ct);
        report.LimitSharePercent = activeUsers > 0
            ? Math.Round(limitUsers * 100.0 / activeUsers, 1)
            : null;

        // --- Conversion: /start users in the window -> created any search ever ---
        var startedUserIds = await _dbContext.UsageEvents
            .Where(e => e.EventType == UsageEventTypes.BotStarted && e.OccurredAtUtc >= periodStart)
            .Select(e => e.UserId)
            .Distinct()
            .ToListAsync(ct);
        report.BotStartsInPeriod = startedUserIds.Count;
        if (startedUserIds.Count > 0)
        {
            var convertedUserIds = await _dbContext.UsageEvents
                .Where(e => e.EventType == UsageEventTypes.SearchCreated)
                .Select(e => e.UserId)
                .Distinct()
                .ToListAsync(ct);

            var startedSet = startedUserIds.ToHashSet();
            startedSet.IntersectWith(convertedUserIds);
            report.ConvertedToFirstSearch = startedSet.Count;
            report.ConversionPercent = Math.Round(report.ConvertedToFirstSearch * 100.0 / startedUserIds.Count, 1);
        }

        return report;
    }

    private Task<int> CountActiveUsersAsync(DateTime since, CancellationToken ct)
    {
        return _dbContext.UsageEvents
            .Where(e => e.OccurredAtUtc >= since)
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync(ct);
    }

    /// <summary>eph-18: top-5 grouping for pre-extracted property values.</summary>
    private static IReadOnlyList<TopUsageItem> TopItems(IEnumerable<string?> values)
    {
        return values
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(g => new TopUsageItem(g.Key, g.Count()))
            .ToList();
    }

    private static string? ParseProp(string? json, string propKey)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return doc.RootElement.TryGetProperty(propKey, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
