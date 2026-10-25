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
    private readonly ConcurrentDictionary<long, UserSearchDraft> _drafts = new();

    public UserSearchDraft GetOrCreateDraft(long userId)
    {
        return _drafts.GetOrAdd(userId, _ => new UserSearchDraft());
    }

    public UserSearchDraft? GetDraft(long userId)
    {
        _drafts.TryGetValue(userId, out var draft);
        return draft;
    }

    public void ResetDraft(long userId)
    {
        _drafts.TryRemove(userId, out _);
    }
}
