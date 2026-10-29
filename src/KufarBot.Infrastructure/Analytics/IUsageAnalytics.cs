using KufarBot.Core.Analytics;

namespace KufarBot.Infrastructure.Analytics;

/// <summary>Report period for /admin analytics (rolling windows ending "now", UTC).</summary>
public enum AnalyticsPeriod
{
    Day,
    Week,
    Month
}

/// <summary>One "top N" row grouped by a PropertiesJson key.</summary>
public sealed record TopUsageItem(string Key, int Count);

/// <summary>Computed usage metrics snapshot for an /admin stats report (eph-17).</summary>
public sealed class UsageReport
{
    public AnalyticsPeriod Period { get; set; }

    /// <summary>Users whose UserAccounts row was created within the period window.</summary>
    public int NewUsers { get; set; }

    /// <summary>Distinct users with any recorded event within the last 1/7/30 days.</summary>
    public int ActiveUsers1d { get; set; }
    public int ActiveUsers7d { get; set; }
    public int ActiveUsers30d { get; set; }

    /// <summary>Total non-paused subscriptions right now.</summary>
    public int ActiveSearches { get; set; }

    /// <summary>Successfully delivered notifications within the period window.</summary>
    public int NotificationsSent { get; set; }

    /// <summary>Top site-filter presets (preset property of PresetSelected) in the window.</summary>
    public IReadOnlyList<TopUsageItem> TopPresets { get; set; } = Array.Empty<TopUsageItem>();

    /// <summary>Top bot features (feature property of FeatureUsed) in the window.</summary>
    public IReadOnlyList<TopUsageItem> TopFeatures { get; set; } = Array.Empty<TopUsageItem>();

    /// <summary>% of active users (window) that hit the subscription limit, null when no active users.</summary>
    public double? LimitSharePercent { get; set; }

    /// <summary>Distinct users that ran /start within the window.</summary>
    public int BotStartsInPeriod { get; set; }

    /// <summary>Of BotStartsInPeriod users — those that have created at least one search ever.</summary>
    public int ConvertedToFirstSearch { get; set; }

    public double? ConversionPercent { get; set; }
}

/// <summary>eph-17: writes usage events and computes aggregate statistics.</summary>
public interface IUsageAnalytics
{
    /// <summary>
    /// Best-effort event recording: must never throw (analytics failures must not
    /// break search creation, delivery, or any other business flow).
    /// </summary>
    Task RecordAsync(long userId, string eventType, object? properties = null, CancellationToken ct = default);

    Task<UsageReport> GetStatsAsync(AnalyticsPeriod period, CancellationToken ct = default, DateTime? nowUtc = null);
}
