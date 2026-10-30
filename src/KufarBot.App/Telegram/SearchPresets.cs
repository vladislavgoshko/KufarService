using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KufarBot.Core.Filters;
using Microsoft.Extensions.Options;

namespace KufarBot.App.Telegram;

/// <summary>eph-18: configuration of the JSON preset catalog location.</summary>
public sealed class SearchPresetOptions
{
    public const string SectionName = "SearchPresets";
    public string Path { get; set; } = "Data/search-presets.json";
}

/// <summary>eph-18: a ready-made search preset. <see cref="Filters"/> reuses the
/// ordinary SavedFilter format (city/price stay null unless the preset justifies
/// them), so creation goes through the standard SaveSearchAsync machinery.</summary>
public sealed class SearchPreset
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Display-only grouping for the catalog menu (not a Kufar id).</summary>
    public string Category { get; init; } = "";
    public int SubcategoryId { get; init; }
    public string SubcategoryTitle { get; init; } = "";
    public int? CityId { get; init; }
    public string? CityName { get; init; }
    public decimal? MinPriceByn { get; init; }
    public decimal? MaxPriceByn { get; init; }
    public IReadOnlyList<SavedFilter> Filters { get; init; } = Array.Empty<SavedFilter>();
    public int SortOrder { get; init; }
}

/// <summary>eph-18: read-only catalog of ready-made search presets.</summary>
public interface ISearchPresetCatalog
{
    IReadOnlyList<SearchPreset> Presets { get; }
    IReadOnlyList<string> Categories { get; }
    IReadOnlyList<SearchPreset> ByCategory(string category);
    SearchPreset? FindById(string id);
}

public sealed class SearchPresetCatalog : ISearchPresetCatalog
{
    private static readonly Regex IdPattern = new("^[a-z0-9-]{1,24}$", RegexOptions.Compiled);

    private readonly List<SearchPreset> _presets = new();

    public SearchPresetCatalog(IOptions<SearchPresetOptions> options)
        : this(options.Value.Path)
    {
    }

    /// <summary>Test/UI convenience: loads the default configured path.</summary>
    public SearchPresetCatalog()
        : this(new SearchPresetOptions().Path)
    {
    }

    public SearchPresetCatalog(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return; // empty catalog — feature degrades gracefully
            }

            var json = File.ReadAllText(path);
            var rows = JsonSerializer.Deserialize<List<SearchPresetFileRow>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<SearchPresetFileRow>();

            _presets = rows
                .Where(r => r.IsActive &&
                            IdPattern.IsMatch(r.Id ?? string.Empty) &&
                            !string.IsNullOrWhiteSpace(r.Name) &&
                            r.SubcategoryId > 0)
                .Select(r => new SearchPreset
                {
                    Id = r.Id,
                    Name = r.Name.Trim(),
                    Description = r.Description ?? string.Empty,
                    Category = string.IsNullOrWhiteSpace(r.Category) ? "Прочее" : r.Category.Trim(),
                    SubcategoryId = r.SubcategoryId,
                    SubcategoryTitle = r.SubcategoryTitle ?? $"Подкатегория {r.SubcategoryId}",
                    CityId = r.CityId,
                    CityName = r.CityName,
                    MinPriceByn = r.MinPriceByn,
                    MaxPriceByn = r.MaxPriceByn,
                    Filters = BuildFilters(r),
                    SortOrder = r.SortOrder
                })
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception)
        {
            // a broken catalog file must not crash the bot — catalog degrades to empty
            _presets = new List<SearchPreset>();
        }
    }

    private static List<SavedFilter> BuildFilters(SearchPresetFileRow r)
    {
        var filters = new List<SavedFilter>();

        // Server-side URL filters (e.g. typ=let/sell — deal type).
        foreach (var f in r.UrlFilters ?? new List<PresetUrlFilter>())
        {
            if (f == null || string.IsNullOrWhiteSpace(f.Key) || string.IsNullOrWhiteSpace(f.Value))
            {
                continue;
            }

            filters.Add(new SavedFilter
            {
                Kind = SavedFilterKind.Single,
                Key = f.Key.Trim(),
                UrlName = f.Key.Trim(),
                Title = f.Title ?? f.Key.Trim(),
                Values = new[] { f.Value.Trim() }
            });
        }

        if (r.QueryWords is { Count: > 0 })
        {
            var words = r.QueryWords
                .SelectMany(w => (w ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(w => w.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (words.Count > 0)
            {
                filters.Add(new SavedFilter
                {
                    Kind = SavedFilterKind.Query,
                    Key = "own_query",
                    Title = "Текст поиска",
                    Values = words
                });
            }
        }

        if (r.KeywordsExclude is { Count: > 0 })
        {
            var words = r.KeywordsExclude
                .Select(w => (w ?? string.Empty).Trim())
                .Where(w => w.Length > 0)
                .ToList();
            if (words.Count > 0)
            {
                filters.Add(new SavedFilter
                {
                    Kind = SavedFilterKind.KeywordsExclude,
                    Key = "own_kw_exc",
                    Title = "Исключить слова",
                    Values = words
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(r.SellerType) &&
            (r.SellerType.Equals("private", StringComparison.OrdinalIgnoreCase) ||
             r.SellerType.Equals("company", StringComparison.OrdinalIgnoreCase)))
        {
            filters.Add(new SavedFilter
            {
                Kind = SavedFilterKind.SellerType,
                Key = "own_seller",
                Title = "Продавец",
                Values = new[] { r.SellerType.ToLowerInvariant() }
            });
        }

        return filters;
    }

    public IReadOnlyList<SearchPreset> Presets => _presets;

    public IReadOnlyList<string> Categories => _presets
        .GroupBy(p => p.Category, StringComparer.Ordinal)
        .OrderBy(g => g.First().SortOrder)
        .Select(g => g.Key)
        .ToList();

    public IReadOnlyList<SearchPreset> ByCategory(string category) => _presets
        .Where(p => string.Equals(p.Category, category, StringComparison.Ordinal))
        .ToList();

    public SearchPreset? FindById(string id) => _presets
        .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
}

internal sealed class SearchPresetFileRow
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("subcategoryId")] public int SubcategoryId { get; set; }
    [JsonPropertyName("subcategoryTitle")] public string SubcategoryTitle { get; set; } = "";
    [JsonPropertyName("cityId")] public int? CityId { get; set; }
    [JsonPropertyName("cityName")] public string? CityName { get; set; }
    [JsonPropertyName("minPriceByn")] public decimal? MinPriceByn { get; set; }
    [JsonPropertyName("maxPriceByn")] public decimal? MaxPriceByn { get; set; }
    [JsonPropertyName("urlFilters")] public List<PresetUrlFilter>? UrlFilters { get; set; }
    [JsonPropertyName("queryWords")] public List<string>? QueryWords { get; set; }
    [JsonPropertyName("keywordsExclude")] public List<string>? KeywordsExclude { get; set; }
    /// <summary>"private" | "company" (own client-side filter).</summary>
    [JsonPropertyName("sellerType")] public string? SellerType { get; set; }
    [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    [JsonPropertyName("isActive")] public bool IsActive { get; set; } = true;
}

internal sealed class PresetUrlFilter
{
    /// <summary>Kufar URL filter name (urlName), e.g. "typ".</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
}
