using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace KufarBot.Core.Filters;

public enum SavedFilterKind
{
    /// <summary>Server-side boolean parameter (client-checked against ad parameters).</summary>
    Bool,
    /// <summary>Server-side single-choice parameter.</summary>
    Single,
    /// <summary>Server-side multi-choice parameter ("any of").</summary>
    Multi,
    /// <summary>Numeric range parameter.</summary>
    Range,
    /// <summary>Free-text parameter (substring match).</summary>
    Text,

    /// <summary>Bot's own filter: any of the keywords must appear in title/description.</summary>
    KeywordsInclude,
    /// <summary>Bot's own filter: none of the keywords may appear in title/description.</summary>
    KeywordsExclude,
    /// <summary>Bot's own filter: private seller or company only ("private" | "company").</summary>
    SellerType,
    /// <summary>Bot's own filter: ad must be younger than N hours (Min holds the hours).</summary>
    MaxAgeHours,
    /// <summary>Bot's own query filter: ALL words must appear in title/description.</summary>
    Query
}

/// <summary>
/// A filter as chosen by the user for a saved search. Serialized to/searched from
/// UserSearchEntity.FiltersJson. All checks happen client-side against the normalized
/// advertisement snapshot — this keeps shared polling feeds intact for all users.
/// </summary>
public sealed record SavedFilter
{
    public required SavedFilterKind Kind { get; init; }
    public required string Key { get; init; }
    /// <summary>
    /// Kufar search-API query parameter name (dispatch <c>url_name</c>, e.g. "cnd" for "condition").
    /// Server filters without it stay client-side only (saved rows from older versions).
    /// </summary>
    public string? UrlName { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }

    public static string Serialize(IEnumerable<SavedFilter> filters) =>
        JsonSerializer.Serialize(filters, SavedFiltersJsonOptions);

    public static List<SavedFilter> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<SavedFilter>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<SavedFilter>>(json, SavedFiltersJsonOptions) ?? new List<SavedFilter>();
        }
        catch
        {
            return new List<SavedFilter>();
        }
    }

    public static readonly JsonSerializerOptions SavedFiltersJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
