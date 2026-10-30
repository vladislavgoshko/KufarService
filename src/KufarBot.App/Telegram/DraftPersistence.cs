using System;
using System.Text.Json;
using KufarBot.Infrastructure.Persistence;

namespace KufarBot.App.Telegram;

/// <summary>
/// eph-10: persists the search draft (eph state of the create-search wizard) into
/// UserEntity.DraftJson so it survives restarts and session eviction.
/// </summary>
public static class DraftPersistence
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Serialized draft for the DB (transient input states cleared; null for an empty draft).</summary>
    public static string? Serialize(UserSearchDraft? draft)
    {
        if (draft == null || !HasContent(draft))
        {
            return null;
        }

        // Text-input expectations must not survive a restart: the next free-text message
        // would otherwise be silently treated as a price / filter value.
        var clone = new UserSearchDraft
        {
            CategoryId = draft.CategoryId,
            CategoryTitle = draft.CategoryTitle,
            SubcategoryId = draft.SubcategoryId,
            SubcategoryTitle = draft.SubcategoryTitle,
            CityId = draft.CityId,
            CityName = draft.CityName,
            MinPriceByn = draft.MinPriceByn,
            MaxPriceByn = draft.MaxPriceByn,
            AwaitingPriceInput = PriceInputMode.None,
            AwaitingFilterInput = null,
            SubscriptionsPage = draft.SubscriptionsPage,
            PresetId = draft.PresetId,
            Filters = new List<Core.Filters.SavedFilter>(draft.Filters),
            AvailableSpecs = new List<Core.Filters.FilterSpec>(draft.AvailableSpecs),
            Wizard = draft.Wizard,
            EditingSearchId = draft.EditingSearchId
        };

        return JsonSerializer.Serialize(clone, JsonOptions);
    }

    /// <summary>Restores a draft from the DB; returns null for an empty/invalid draft.</summary>
    public static UserSearchDraft? Deserialize(string? json)
    {
        UserSearchDraft? draft;
        try
        {
            draft = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<UserSearchDraft>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (draft == null)
        {
            return null;
        }

        // safety net even for old rows serialized without sanitization
        draft.AwaitingPriceInput = PriceInputMode.None;
        draft.AwaitingFilterInput = null;

        return HasContent(draft) ? draft : null;
    }

    private static bool HasContent(UserSearchDraft draft)
    {
        return draft.CategoryId.HasValue
            || draft.SubcategoryId.HasValue
            || draft.Filters.Count > 0
            || draft.MinPriceByn.HasValue
            || draft.MaxPriceByn.HasValue
            || draft.EditingSearchId.HasValue;
    }
}
