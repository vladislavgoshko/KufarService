using System;
using System.Linq;
using System.Text.Json;
using KufarBot.Core.Filters;
using KufarBot.Core.Http.Dto;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;
using Xunit;

namespace KufarBot.Tests;

public class FilterEngineTests
{
    private const string DispatchRulesJson = @"{""metadata"":{
        ""parameters"":{
            ""dispatch_type"":""many"",
            ""deduplication_key"":""name"",
            ""refs"":{
                ""1"":{""variation_id"":1,""name"":""area"",""url_name"":""ar"",""type"":""int"",""meta"":""set""},
                ""2"":{""variation_id"":2,""name"":""condition"",""url_name"":""cnd"",""type"":""int"",""meta"":""set"",
                    ""labels"":{""name"":{""ru"":""Состояние""}},
                    ""values"":[{""value"":1,""labels"":{""ru"":""Новое""}},{""value"":10,""labels"":{""ru"":""Б/у""}}]},
                ""3"":{""variation_id"":3,""name"":""computers_component_type"",""url_name"":""cct"",""type"":""int"",""meta"":""set"",
                    ""labels"":{""name"":{""ru"":""Тип""}},
                    ""values"":[{""value"":5,""labels"":{""ru"":""Процессоры""}},{""value"":10,""labels"":{""ru"":""Видеокарты""}}]},
                ""4"":{""variation_id"":4,""name"":""socket"",""url_name"":""sck"",""type"":""int"",""meta"":""set"",
                    ""labels"":{""name"":{""ru"":""Сокет""}},
                    ""values"":[{""value"":3,""labels"":{""ru"":""AM4""}}]},
                ""5"":{""variation_id"":5,""name"":""housing_agency"",""url_name"":""ha"",""type"":""bool"",""meta"":""set"",
                    ""labels"":{""name"":{""ru"":""Региональный параметр""}}}
            },
            ""rules"":[
                {""rule"":{},""refs"":[""1"",""2""]},
                {""rule"":{""category"":""16010""},""refs"":[""3""]},
                {""rule"":{""region"":""7""},""refs"":[""5""]},
                {""rule"":{""category"":""16010"",""computers_component_type"":""5""},""refs"":[""4""]}
            ]
        },
        ""parameters_order"":{""refs"":{""0"":[""3"",""2"",""1""]}}}}";

    [Fact]
    public void FilterSpecFactory_ForSubcategoryUsesRulesAndOrder()
    {
        var dispatch = JsonSerializer.Deserialize<KufarParametersResponseDto>(DispatchRulesJson);
        var specs = FilterSpecFactory.FromDispatchForSubcategory(dispatch, 16010);

        // area skipped (builtin), agency skipped (region rule), socket skipped (conditional type rule)
        Assert.Equal(2, specs.Count);

        // parameters_order: "3" (Тип) before "2" (Состояние)
        Assert.Equal("computers_component_type", specs[0].Key);
        Assert.Equal("condition", specs[1].Key);

        Assert.Equal(FilterKind.Single, specs[0].Kind);
        Assert.Equal("Тип", specs[0].Title);
        Assert.Equal(2, specs[0].Options.Count);
    }

    [Fact]
    public void FilterSpecFactory_ForOtherSubcategoryReturnsBaseOnly()
    {
        var dispatch = JsonSerializer.Deserialize<KufarParametersResponseDto>(DispatchRulesJson);
        var specs = FilterSpecFactory.FromDispatchForSubcategory(dispatch, 16020);

        // base rule gives condition (area skipped); subcategory 16010 rule does not apply
        Assert.Single(specs);
        Assert.Equal("condition", specs[0].Key);
    }

    [Fact]
    public void FilterSpecFactory_ForSubcategoryWithoutRulesExposesAll()
    {
        var dispatch = JsonSerializer.Deserialize<KufarParametersResponseDto>(DispatchJson);
        var specs = FilterSpecFactory.FromDispatchForSubcategory(dispatch, 16010);

        // fallback: no rules -> everything except builtins; order list absent -> original order
        Assert.All(specs, s => Assert.NotEqual("area", s.Key));
        Assert.Contains(specs, s => s.Key == "condition");
    }

    private const string DispatchJson = @"{""metadata"":{""parameters"":{
        ""dispatch_type"":""many"",
        ""deduplication_key"":""name"",
        ""refs"":{
            ""143"":{""variation_id"":143,""name"":""area"",""url_name"":""ar"",""required"":true,""type"":""int"",""meta"":""set"",""multi"":false,""labels"":{""name"":{""ru"":""Город""}}},
            ""405"":{""variation_id"":405,""name"":""condition"",""url_name"":""cnd"",""required"":false,""type"":""int"",""meta"":""set"",""multi"":false,
                ""labels"":{""name"":{""ru"":""Состояние""}},
                ""values"":[{""value"":1,""labels"":{""ru"":""Новое""}},{""value"":10,""labels"":{""ru"":""Б/у""}}]},
            ""406"":{""variation_id"":406,""name"":""condition"",""url_name"":""cnd"",""type"":""int"",""meta"":""set""},
            ""147"":{""variation_id"":147,""name"":""only_with_photo"",""url_name"":""oph"",""type"":""bool"",""meta"":""set"",""multi"":false,
                ""labels"":{""name"":{""ru"":""Только с фото""}}},
            ""10042"":{""variation_id"":10042,""name"":""computers_desktop_ram"",""url_name"":""cdr"",""type"":""int"",""meta"":""free"",""multi"":false,
                ""labels"":{""name"":{""ru"":""Объём RAM""}},
                ""range"":{""lower"":4,""step"":2,""upper"":64},
                ""ui_component"":{""type"":""input_range""}},
            ""10098"":{""variation_id"":10098,""name"":""users_synonyms"",""url_name"":""usn"",""type"":""string"",""meta"":""free"",""multi"":false,
                ""labels"":{""name"":{""ru"":""Синонимы""}},
                ""values"":[]}
        }}}}";

    [Fact]
    public void FilterSpecFactory_FlattensDedupesAndSkipsBuiltins()
    {
        var dispatch = JsonSerializer.Deserialize<KufarParametersResponseDto>(DispatchJson);
        var specs = FilterSpecFactory.FromDispatch(dispatch);

        Assert.Equal(2, specs.Count); // area/only_with_photo/users_synonyms skipped, condition deduplicated (first variation wins)
        Assert.All(specs, s => Assert.NotEqual("area", s.Key));
        Assert.All(specs, s => Assert.NotEqual("only_with_photo", s.Key));
        Assert.All(specs, s => Assert.NotEqual("users_synonyms", s.Key));

        var condition = specs.Single(s => s.Key == "condition");
        Assert.Equal(FilterKind.Single, condition.Kind);
        Assert.Equal("cnd", condition.UrlName);
        Assert.Equal("Состояние", condition.Title);
        Assert.Equal(2, condition.Options.Count);
        Assert.Equal("Состояние", condition.Title);
        Assert.Equal("Новое", condition.Options[0].Label);
        Assert.Equal("1", condition.Options[0].Value);

        var ram = specs.Single(s => s.Key == "computers_desktop_ram");
        Assert.Equal(FilterKind.Range, ram.Kind);
        Assert.Equal(4m, ram.RangeLower);
        Assert.Equal(64m, ram.RangeUpper);
    }

    [Fact]
    public void FilterSpecFactory_ByCategoryBuildsMapForSubcategoryRules()
    {
        var dispatch = JsonSerializer.Deserialize<KufarParametersResponseDto>(DispatchRulesJson);
        var map = FilterSpecFactory.FromDispatchByCategory(dispatch);

        // subcategory rule detected; region-bound rule is not a subcategory
        Assert.Single(map);
        Assert.True(map.ContainsKey(16010));

        var specs = map[16010];
        Assert.Equal(2, specs.Count); // base condition + subcategory's Тип, ordered as on the site
        Assert.Equal("computers_component_type", specs[0].Key);
        Assert.Equal("condition", specs[1].Key);
    }

    [Fact]
    public void FilterSpecFactory_ByCategoryWithoutRulesReturnsEmptyMap()
    {
        var dispatch = JsonSerializer.Deserialize<KufarParametersResponseDto>(DispatchJson);
        var map = FilterSpecFactory.FromDispatchByCategory(dispatch);

        Assert.Empty(map);
    }

    [Fact]
    public void FilterSpecFactory_KeepsOnlyWhitelistedBoolFilters()
    {
        var dispatch = JsonSerializer.Deserialize<KufarParametersResponseDto>(DispatchBoolRulesJson);
        var specs = FilterSpecFactory.FromDispatchForSubcategory(dispatch, 16011);

        // possible_exchange is a real ad attribute; gift_new (posting-form bool) is dropped
        Assert.Equal(2, specs.Count);
        Assert.Contains(specs, s => s.Key == "possible_exchange" && s.Kind == FilterKind.Bool);
        Assert.Contains(specs, s => s.Key == "condition");
        Assert.DoesNotContain(specs, s => s.Key == "gift_new");
    }

    private const string DispatchBoolRulesJson = @"{""metadata"":{""parameters"":{
        ""refs"":{
            ""200"":{""variation_id"":200,""name"":""possible_exchange"",""url_name"":""pe"",""type"":""bool"",""meta"":""set"",
                ""labels"":{""name"":{""ru"":""Обмен""}}},
            ""201"":{""variation_id"":201,""name"":""gift_new"",""url_name"":""gn"",""type"":""bool"",""meta"":""set"",
                ""labels"":{""name"":{""ru"":""Подарок""}}},
            ""202"":{""variation_id"":202,""name"":""condition"",""url_name"":""cnd"",""type"":""int"",""meta"":""set"",
                ""labels"":{""name"":{""ru"":""Состояние""}},
                ""values"":[{""value"":1,""labels"":{""ru"":""Новое""}}]}
        },
        ""rules"":[{""rule"":{""category"":""16011""},""refs"":[""200"",""201"",""202""]}]
    }}}";

    private static UserSearchEntity MakeSearch(params SavedFilter[] filters)
    {
        return new UserSearchEntity
        {
            Id = 1,
            UserId = 1,
            SubcategoryId = 16010,
            FiltersJson = SavedFilter.Serialize(filters),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static Advertisement MakeAd(
        string title = "GPU",
        decimal? price = 100m,
        bool? companyAd = null,
        DateTime? listTime = null,
        params AdParameter[] parameters)
    {
        return new Advertisement
        {
            Id = 1,
            Title = title,
            PriceByn = price,
            Location = "Минск",
            AdLink = "link",
            CompanyAd = companyAd,
            ListTimeUtc = listTime ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Parameters = parameters
        };
    }

    [Fact]
    public void Matches_KeywordsIncludeExclude()
    {
        var search = MakeSearch(
            new SavedFilter { Kind = SavedFilterKind.KeywordsInclude, Key = "custom", Title = "Включить", Values = new[] { "gtx", "3080" } },
            new SavedFilter { Kind = SavedFilterKind.KeywordsExclude, Key = "custom2", Title = "Исключить", Values = new[] { "битая" } });

        Assert.True(search.Matches(MakeAd("Видеокарта MSI GTX 1060")));
        Assert.False(search.Matches(MakeAd("Видеокарта Radeon"))); // no include keyword
        Assert.False(search.Matches(MakeAd("GTX 1060 битая")));   // excluded keyword
    }

    [Fact]
    public void Matches_SellerType()
    {
        var privateSearch = MakeSearch(new SavedFilter { Kind = SavedFilterKind.SellerType, Key = "custom", Title = "Продавец", Values = new[] { "private" } });
        var companySearch = MakeSearch(new SavedFilter { Kind = SavedFilterKind.SellerType, Key = "custom", Title = "Продавец", Values = new[] { "company" } });

        Assert.True(privateSearch.Matches(MakeAd(companyAd: false)));
        Assert.False(privateSearch.Matches(MakeAd(companyAd: true)));
        Assert.True(companySearch.Matches(MakeAd(companyAd: true)));
        Assert.False(companySearch.Matches(MakeAd(companyAd: false)));
    }

    [Fact]
    public void Matches_MaxAgeHours()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var search = MakeSearch(new SavedFilter { Kind = SavedFilterKind.MaxAgeHours, Key = "custom", Title = "Возраст", Min = 2m });

        Assert.True(search.Matches(MakeAd(listTime: now.AddHours(-1)), utcNow: now));
        Assert.False(search.Matches(MakeAd(listTime: now.AddHours(-3)), utcNow: now));
    }

    [Fact]
    public void Matches_ServerFiltersAgainstAdParameters()
    {
        var search = MakeSearch(
            new SavedFilter { Kind = SavedFilterKind.Single, Key = "condition", Title = "Состояние", Values = new[] { "1" } },
            new SavedFilter { Kind = SavedFilterKind.Bool, Key = "only_with_photo", Title = "Только с фото", Values = new[] { "1" } },
            new SavedFilter { Kind = SavedFilterKind.Multi, Key = "remuneration_type", Title = "Тип", Values = new[] { "1", "2" } },
            new SavedFilter { Kind = SavedFilterKind.Range, Key = "computers_desktop_ram", Title = "RAM", Min = 8m, Max = 32m });

        var matchingAd = MakeAd("GPU", 100m, null, null,
            new AdParameter("condition", "1", "Новое"),
            new AdParameter("only_with_photo", "1"),
            new AdParameter("remuneration_type", "2"),
            new AdParameter("computers_desktop_ram", "16"));
        Assert.True(search.Matches(matchingAd));

        var wrongCondition = MakeAd("x", 100m, null, null,
            new AdParameter("condition", "10", "Б/у"),
            new AdParameter("only_with_photo", "1"),
            new AdParameter("remuneration_type", "2"),
            new AdParameter("computers_desktop_ram", "16"));
        Assert.False(search.Matches(wrongCondition));

        var noPhoto = MakeAd("x", 100m, null, null,
            new AdParameter("condition", "1", "Новое"),
            new AdParameter("remuneration_type", "2"),
            new AdParameter("computers_desktop_ram", "16"));
        Assert.False(search.Matches(noPhoto)); // bool filter required but parameter missing

        var ramTooSmall = MakeAd("x", 100m, null, null,
            new AdParameter("condition", "1", "Новое"),
            new AdParameter("only_with_photo", "1"),
            new AdParameter("remuneration_type", "2"),
            new AdParameter("computers_desktop_ram", "4"));
        Assert.False(search.Matches(ramTooSmall));
    }

    [Fact]
    public void SavedFilter_RoundtripSerialization()
    {
        var original = new SavedFilter[]
        {
            new() { Kind = SavedFilterKind.Single, Key = "condition", Title = "Состояние", Values = new[] { "1" } },
            new() { Kind = SavedFilterKind.Range, Key = "ram", Title = "RAM", Min = 8m, Max = 32m },
            new() { Kind = SavedFilterKind.MaxAgeHours, Key = "custom", Title = "Возраст", Min = 24m }
        };

        var json = SavedFilter.Serialize(original);
        var restored = SavedFilter.Deserialize(json);

        Assert.Equal(3, restored.Count);
        Assert.Equal(SavedFilterKind.Range, restored[1].Kind);
        Assert.Equal(8m, restored[1].Min);
        Assert.Equal("Состояние", restored[0].Title);

        // Saved search without filters keeps default "[]" and matches everything price-wise
        var noFilters = MakeSearch();
        Assert.True(noFilters.Matches(MakeAd()));
        Assert.Equal("[]", noFilters.FiltersJson);
    }

    [Fact]
    public void RealKufarFixture_ParamsSnapshotPreserved()
    {
        var path = FindFixturePath("research/data/api_data/search16010.json");
        var json = System.IO.File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<KufarSearchResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var ads = KufarAdNormalizer.NormalizeMany(dto!.Ads);
        var gtx = ads.First(a => a.Id == 1052019666L);

        Assert.False(gtx.CompanyAd); // private seller in the fixture
        var names = gtx.Parameters.Select(p => p.Name).ToArray();
        Assert.Contains("condition", names);
        Assert.Contains("computers_component_type", names);

        var condition = gtx.Parameters.First(p => p.Name == "condition");
        Assert.Equal("1", condition.Value);
        Assert.NotNull(condition.Label);
    }

    private static string FindFixturePath(string relativePath)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, relativePath);
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }
        throw new System.IO.FileNotFoundException(relativePath);
    }
}
