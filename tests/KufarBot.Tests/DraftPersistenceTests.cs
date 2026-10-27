using System;
using System.Linq;
using KufarBot.App.Telegram;
using KufarBot.Core.Filters;
using Xunit;

namespace KufarBot.Tests;

/// <summary>eph-10: search drafts persist into UserEntity.DraftJson across restarts.</summary>
public class DraftPersistenceTests
{
    private static UserSearchDraft FullDraft()
    {
        var draft = new UserSearchDraft
        {
            CategoryId = 16000,
            CategoryTitle = "Компьютеры",
            SubcategoryId = 16010,
            SubcategoryTitle = "Комплектующие",
            CityId = 7,
            CityName = "Минск",
            MinPriceByn = 100m,
            MaxPriceByn = 500m,
            SubscriptionsPage = 3,
            EditingSearchId = null
        };

        draft.Filters.Add(new SavedFilter
        {
            Kind = SavedFilterKind.Single,
            Key = "condition",
            UrlName = "cnd",
            Title = "Состояние",
            Values = new[] { "2" }
        });
        draft.Filters.Add(new SavedFilter
        {
            Kind = SavedFilterKind.KeywordsExclude,
            Key = "own_kw_exc",
            Title = "Исключить слова",
            Values = new[] { "тачки" }
        });

        draft.AvailableSpecs.Add(new FilterSpec
        {
            Key = "condition",
            UrlName = "cnd",
            Title = "Состояние",
            Kind = FilterKind.Single,
            Options = new[] { new FilterOption("1", "Новое"), new FilterOption("2", "Б/у") }
        });
        draft.AvailableSpecs.Add(new FilterSpec
        {
            Key = "computers_desktop_frequency",
            UrlName = "cdf",
            Title = "Частота",
            Kind = FilterKind.Range,
            RangeLower = 0m,
            RangeUpper = 7m
        });

        return draft;
    }

    [Fact]
    public void SerializeDeserialize_RoundtripsCoreState()
    {
        var draft = FullDraft();

        string json = DraftPersistence.Serialize(draft);
        Assert.False(string.IsNullOrWhiteSpace(json));

        var restored = DraftPersistence.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(draft.CategoryId, restored!.CategoryId);
        Assert.Equal(draft.SubcategoryId, restored.SubcategoryId);
        Assert.Equal(draft.CityId, restored.CityId);
        Assert.Equal(draft.MinPriceByn, restored.MinPriceByn);
        Assert.Equal(draft.MaxPriceByn, restored.MaxPriceByn);
        Assert.Equal(2, restored.Filters.Count);
        Assert.Equal("cnd", restored.Filters[0].UrlName);
        Assert.Equal(2, restored.AvailableSpecs.Count);
        Assert.Equal("cdf", restored.AvailableSpecs[1].UrlName);
    }

    [Fact]
    public void Serialize_ClearsTransientInputStates()
    {
        var draft = FullDraft();
        draft.AwaitingPriceInput = PriceInputMode.MaxPrice;
        draft.AwaitingFilterInput = -11;
        draft.Wizard = new FilterWizardState { Page = 2, SpecIndex = 0 };
        draft.EditingSearchId = 42;

        var restored = DraftPersistence.Deserialize(DraftPersistence.Serialize(draft))!;

        Assert.Equal(PriceInputMode.None, restored.AwaitingPriceInput);
        Assert.Null(restored.AwaitingFilterInput);
        Assert.Equal(42, restored.EditingSearchId);
    }

    [Fact]
    public void Serialize_EmptyDraft_ReturnsNull()
    {
        Assert.Null(DraftPersistence.Serialize(null));
        Assert.Null(DraftPersistence.Serialize(new UserSearchDraft()));

        var draft = new UserSearchDraft();
        draft.UserIdIndependentFieldsOnly();
        Assert.Null(DraftPersistence.Serialize(draft));
    }

    [Fact]
    public void Deserialize_InvalidOrEmptyJson_ReturnsNull()
    {
        Assert.Null(DraftPersistence.Deserialize(null));
        Assert.Null(DraftPersistence.Deserialize(""));
        Assert.Null(DraftPersistence.Deserialize("not a json at all"));
        Assert.Null(DraftPersistence.Deserialize("{}"));
    }
}

internal static class UserSearchDraftTestExtensions
{
    /// <summary>Makes the draft "page-only" (navigation without content) — must not be persisted.</summary>
    public static UserSearchDraft UserIdIndependentFieldsOnly(this UserSearchDraft draft)
    {
        draft.SubscriptionsPage = 1;
        return draft;
    }
}
