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
