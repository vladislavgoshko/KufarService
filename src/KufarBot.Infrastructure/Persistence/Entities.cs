using System;
using System.Collections.Generic;

namespace KufarBot.Infrastructure.Persistence;

public class UserEntity
{
    public long Id { get; set; } // Telegram ChatId
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>eph-10: serialized search draft (search-wizard state) — survives bot restarts.</summary>
    public string? DraftJson { get; set; }

    /// <summary>eph-19: user's last chosen search city — pre-selected at the next create/edit.</summary>
    public int? LastCityId { get; set; }

    /// <summary>eph-19: display name of the last chosen city (null = «Вся Беларусь»).</summary>
    public string? LastCityName { get; set; }

    public ICollection<UserSearchEntity> Searches { get; set; } = new List<UserSearchEntity>();
    public ICollection<DeliveredAdEntity> DeliveredAds { get; set; } = new List<DeliveredAdEntity>();
    public ICollection<NotificationOutboxEntity> OutboxItems { get; set; } = new List<NotificationOutboxEntity>();
}

public class UserSearchEntity
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public UserEntity User { get; set; } = null!;

    public int SubcategoryId { get; set; }
    public string SubcategoryTitle { get; set; } = string.Empty;
    public int? CityId { get; set; }
    public string? CityName { get; set; }
    public decimal? MinPriceByn { get; set; }
    public decimal? MaxPriceByn { get; set; }
    public bool IsPaused { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>JSON array of SavedFilter (KufarBot.Core.Filters). "[]" = no filters.</summary>
    public string FiltersJson { get; set; } = "[]";
}

public class PollingFeedEntity
{
    public int Id { get; set; }
    public int SubcategoryId { get; set; }
    public int? CityId { get; set; } // null = All Belarus
    /// <summary>Canonical JSON of the URL filter dictionary (KufarBot.Core.Filters.UrlFiltersBuilder.CanonicalJson). "{}" = no URL filters.</summary>
    public string UrlFiltersJson { get; set; } = "{}";
    public DateTime? LastPolledAtUtc { get; set; }
    public DateTime LastSeenAdTimeUtc { get; set; }
    public string LastSeenAdIdsJson { get; set; } = "[]"; // JSON array of long IDs
    public int ConsecutiveErrors { get; set; } = 0;
    /// <summary>eph-13: UTC time of the last failed poll — reference point for the exponential backoff skip.</summary>
    public DateTime? LastErrorAtUtc { get; set; }
}

public class DeliveredAdEntity
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public long AdId { get; set; }
    public int MatchedSearchId { get; set; }
    public DateTime DeliveredAtUtc { get; set; }
}

/// <summary>
/// An ad the user added to favorites (⭐). A worker re-checks it periodically:
/// a price drop triggers an outbox notification, disappearance is reported once.
/// </summary>
public class WatchedAdEntity
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public UserEntity User { get; set; } = null!;

    public long AdId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AdLink { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Location { get; set; }

    public decimal? LastPriceByn { get; set; }
    public DateTime LastCheckedUtc { get; set; }
    public bool NotifiedMissing { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class NotificationOutboxEntity
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public int SearchId { get; set; }
    public long AdId { get; set; }
    public int Status { get; set; } // 0=Pending, 1=Sending, 2=Sent, 3=Failed, 4=DeadLetter
    public string PayloadJson { get; set; } = string.Empty;
    public int AttemptCount { get; set; } = 0;
    public int MaxAttempts { get; set; } = 5;
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
}

/// <summary>eph-17: usage analytics event. Plain event log — no FK to Users on
/// purpose, so events survive and the identity/system of records stays in the
/// existing Users / UserSearches tables.</summary>
public class UsageEventEntity
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? PropertiesJson { get; set; }
}

/// <summary>eph-18: a user's personal search template — a copy of search parameters
/// (subcategory, city, prices, SavedFilter JSON) that can spawn ordinary searches.
/// Strictly per-user: editing/renaming never touches the shared preset catalog.</summary>
public class UserTemplateEntity
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int SubcategoryId { get; set; }
    public string SubcategoryTitle { get; set; } = string.Empty;
    public int? CityId { get; set; }
    public string? CityName { get; set; }
    public decimal? MinPriceByn { get; set; }
    public decimal? MaxPriceByn { get; set; }
    public string FiltersJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
