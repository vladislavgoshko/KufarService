using System.Collections.Concurrent;

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
