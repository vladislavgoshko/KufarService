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

    /// <summary>Preset that seeded this draft, if any (eph-18: lets draft_save attribute the creation).</summary>
    public string? PresetId { get; set; }

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

    /// <summary>eph-18: template id awaiting a rename via free-text message (per user).</summary>
    private readonly ConcurrentDictionary<long, int> _pendingTemplateRename = new();

    public void SetPendingTemplateRename(long userId, int templateId) =>
        _pendingTemplateRename[userId] = templateId;

    public int? PeekPendingTemplateRename(long userId) =>
        _pendingTemplateRename.TryGetValue(userId, out var id) ? id : null;

    public int? TakePendingTemplateRename(long userId) =>
        _pendingTemplateRename.TryRemove(userId, out var id) ? id : null;

    // ===================== eph-19: global "last chosen city" memory =====================

    /// <summary>Last city chosen by the user (eph-19): pre-fills fresh drafts
    /// (New search after /start or a saved draft), surviving restarts via Users.LastCity*.
    /// </summary>
    private readonly ConcurrentDictionary<long, (int? CityId, string? CityName)> _lastCities = new();

    public void SetLastCity(long userId, int? cityId, string? cityName) =>
        _lastCities[userId] = (cityId, cityName);

    public (int? CityId, string? CityName)? GetLastCity(long userId) =>
        _lastCities.TryGetValue(userId, out var city) ? city : null;

    public UserSearchDraft GetOrCreateDraft(long userId)
    {
        PruneStaleSessions();
        var draft = _drafts.AddOrUpdate(
            userId,
            _ =>
            {
                // a brand-new draft starts with the user's last chosen city (eph-19)
                var fresh = new UserSearchDraft();
                if (_lastCities.TryGetValue(userId, out var last))
                {
                    fresh.CityId = last.CityId;
                    fresh.CityName = last.CityName;
                }

                return (fresh, DateTime.UtcNow);
            },
            (_, existing) => (existing.Draft, DateTime.UtcNow)).Draft;
        return draft;
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
