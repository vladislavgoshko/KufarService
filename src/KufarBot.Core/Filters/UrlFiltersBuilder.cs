using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace KufarBot.Core.Filters;

/// <summary>
/// Converts server-side SavedFilters into query parameters for the Kufar search API
/// (search-api/v2/search/rendered-paginated). Empirically verified formats:
///   single:      cnd=2
///   boolean:     pse=1
///   multi (OR):  cct=v.or:11,5        (site builds it the same way — ui_component type "multiselect_or")
///   multi (AND): cct=v.and:11,5
///   range:       prc=r:100,2000       (both bounds mandatory; missing side is filled)
/// Unknown query parameter names make the API reject the whole request, so the polling
/// worker keeps a fallback: retry without URL filters (client-side matching covers it).
/// </summary>
public static class UrlFiltersBuilder
{
    /// <summary>Upper bound used when the user did not enter a max value.</summary>
    public const decimal DefaultRangeUpper = 999_999_999m;

    public static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    /// <summary>
    /// Server-side filters that can be expressed in the search URL.
    /// Bot's own filters (keywords, seller, age, query) are checked client-side per user.
    /// Filters without UrlName (legacy rows) stay client-side too.
    /// </summary>
    public static bool IsUrlFilter(SavedFilter filter)
    {
        return filter.Kind is SavedFilterKind.Bool or SavedFilterKind.Single
            or SavedFilterKind.Multi or SavedFilterKind.Range or SavedFilterKind.Text;
    }

    /// <summary>Maps SavedFilters to {urlName → raw query value} (not URL-encoded yet).</summary>
    public static Dictionary<string, string> Build(IEnumerable<SavedFilter> filters)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var filter in filters)
        {
            if (!IsUrlFilter(filter) || string.IsNullOrWhiteSpace(filter.UrlName))
            {
                continue;
            }

            string? value = EncodeValue(filter);
            if (string.IsNullOrEmpty(value) || result.ContainsKey(filter.UrlName))
            {
                continue;
            }

            result[filter.UrlName] = value;
        }

        return result;
    }

    /// <summary>Encodes the value(s) of one filter into the format of the Kufar search API.</summary>
    public static string? EncodeValue(SavedFilter filter)
    {
        switch (filter.Kind)
        {
            case SavedFilterKind.Bool:
            case SavedFilterKind.Single:
                return filter.Values.FirstOrDefault();

            case SavedFilterKind.Multi:
                var values = filter.Values
                    .Where(v => !string.IsNullOrWhiteSpace(v) && !v.Contains(','))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return values.Count > 0 ? $"v.or:{string.Join(",", values)}" : null;

            case SavedFilterKind.Range:
                decimal min = filter.Min ?? 0m;
                decimal max = filter.Max ?? DefaultRangeUpper;
                if (min > max)
                {
                    (min, max) = (max, min);
                }

                return FormattableString.Invariant($"r:{min:0.########},{max:0.########}");

            case SavedFilterKind.Text:
                string text = filter.Values.FirstOrDefault() ?? "";
                return text.Length > 0 && !text.Contains(',') ? text : null;

            default:
                return null; // bot's own filters are checked client-side
        }
    }

    /// <summary>
    /// Canonical JSON used as the polling-feed identity for the URL filter set:
    /// keys sorted, resolved to "{}" when empty. Stable across app restarts.
    /// </summary>
    public static string CanonicalJson(IReadOnlyDictionary<string, string> urlFilters)
    {
        var sorted = urlFilters.ToSortedDictionary();
        if (sorted.Count == 0)
        {
            return "{}";
        }

        string json = JsonSerializer.Serialize(sorted);
        return json;
    }

    /// <summary>Parses a stored feed identity back into the URL filter dictionary.</summary>
    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static SortedDictionary<string, string> ToSortedDictionary(this IReadOnlyDictionary<string, string> source)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            sorted[pair.Key] = pair.Value;
        }

        return sorted;
    }
}
