using System.Collections.Concurrent;
using System.Collections.Generic;
using KufarBot.Core.Filters;

namespace KufarBot.App.Telegram;

public enum PriceInputMode
{
    None,
    MinPrice,
    MaxPrice
}

public class UserSearchDraft
{
    public int? CategoryId { get; set; }
    public string? CategoryTitle { get; set; }
    public int? SubcategoryId { get; set; }
    public string? SubcategoryTitle { get; set; }
    public int? CityId { get; set; }
    public string? CityName { get; set; }
    public decimal? MinPriceByn { get; set; }
    public decimal? MaxPriceByn { get; set; }
    public PriceInputMode AwaitingPriceInput { get; set; } = PriceInputMode.None;

    /// <summary>Chosen filters (server-side + bot's own), persisted with the search.</summary>
    public List<SavedFilter> Filters { get; set; } = new();

    /// <summary>Filter specs fetched from Kufar for the chosen subcategory (cached in draft).</summary>
    public List<FilterSpec> AvailableSpecs { get; set; } = new();

    /// <summary>Non-null while the filter wizard is open.</summary>
    public FilterWizardState? Wizard { get; set; }

    /// <summary>
    /// null = normal value input for spec (index) of kind Range/Text;
    /// -10/-11 = keyword include/exclude input.
    /// </summary>
    public int? AwaitingFilterInput { get; set; }

    /// <summary>When set, the wizard edits an existing subscription (its filters) instead of creating a new one.</summary>
    public int? EditingSearchId { get; set; }

    /// <summary>Current page of the subscriptions list (8 per page).</summary>
    public int SubscriptionsPage { get; set; }
}

public class FilterWizardState
{
    public int Page { get; set; }
    public int SpecIndex { get; set; } = -1;
    public int SelectedValuesPage { get; set; }
}

public class UserSessionStore
{
    private const int MaxTrackedSessions = 500;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<long, (UserSearchDraft Draft, DateTime LastActivityUtc)> _drafts = new();

    public UserSearchDraft GetOrCreateDraft(long userId)
    {
        PruneStaleSessions();
        return _drafts.AddOrUpdate(
            userId,
            _ => (new UserSearchDraft(), DateTime.UtcNow),
            (_, existing) => (existing.Draft, DateTime.UtcNow)).Draft;
    }

    public UserSearchDraft? GetDraft(long userId)
    {
        if (_drafts.TryGetValue(userId, out var existing))
        {
            _drafts[userId] = (existing.Draft, DateTime.UtcNow);
            return existing.Draft;
        }

        return null;
    }

    public void ResetDraft(long userId)
    {
        _drafts.TryRemove(userId, out _);
    }

    /// <summary>Replaces the existing draft (used when editing an existing subscription's filters).</summary>
    public void SetDraft(long userId, UserSearchDraft draft)
    {
        _drafts[userId] = (draft, DateTime.UtcNow);
    }

    private void PruneStaleSessions()
    {
        if (_drafts.Count < MaxTrackedSessions)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - StaleAfter;
        foreach (var kv in _drafts)
        {
            if (kv.Value.LastActivityUtc < cutoff)
            {
                _drafts.TryRemove(kv.Key, out _);
            }
        }
    }
}
