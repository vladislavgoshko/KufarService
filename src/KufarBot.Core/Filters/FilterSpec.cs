using System.Collections.Generic;
using System.Linq;
using KufarBot.Core.Http.Dto;

namespace KufarBot.Core.Filters;

public enum FilterKind
{
    Bool,
    Single,
    Multi,
    Range,
    Text
}

public sealed record FilterOption(string Value, string Label);

/// <summary>
/// Normalized description of a Kufar category parameter, independent of the dispatch JSON shape.
/// Key = parameter name (e.g. "condition") — the same key used by ad parameter snapshots,
/// so server-side filters can also be checked client-side against fetched ads.
/// </summary>
public sealed record FilterSpec
{
    public required string Key { get; init; }
    public string? UrlName { get; init; }
    public required string Title { get; init; }
    public required FilterKind Kind { get; init; }
    public bool Multi { get; init; }
    public IReadOnlyList<FilterOption> Options { get; init; } = System.Array.Empty<FilterOption>();
    public decimal? RangeLower { get; init; }
    public decimal? RangeUpper { get; init; }
}

public static class FilterSpecFactory
{
    private static readonly HashSet<string> BuiltInParameterNames = new()
    {
        "region",
        "area",
        "category"
    };

    /// <summary>
    /// Flattens the Kufar dispatch metadata into FilterSpecs.
    /// Variations are deduplicated by parameter name (per the dispatch "deduplication_key": "name").
    /// Rules-based conditional parameters are not interpreted yet — every parameter from refs is offered.
    /// </summary>
    public static List<FilterSpec> FromDispatch(KufarParametersResponseDto? dispatch)
    {
        var container = dispatch?.Metadata?.Parameters;
        if (container?.Refs == null)
        {
            return new List<FilterSpec>();
        }

        var result = new Dictionary<string, FilterSpec>(System.StringComparer.Ordinal);
        foreach (var param in container.Refs.Values)
        {
            if (string.IsNullOrWhiteSpace(param.Name) || BuiltInParameterNames.Contains(param.Name))
            {
                continue;
            }

            if (result.ContainsKey(param.Name))
            {
                continue; // first variation wins
            }

            var spec = BuildSpec(param);
            if (spec != null)
            {
                result[param.Name] = spec;
            }
        }

        return result.Values.ToList();
    }

    private static FilterSpec? BuildSpec(KufarParameterDto param)
    {
        string ruTitle = param.Labels?.Name?.Ru?.Trim() ?? param.Name ?? "Параметр";
        string urlName = string.IsNullOrWhiteSpace(param.UrlName) ? param.Name! : param.UrlName!.Trim();

        if (string.Equals(param.Type, "bool", System.StringComparison.OrdinalIgnoreCase))
        {
            return new FilterSpec
            {
                Key = param.Name!,
                UrlName = urlName,
                Title = ruTitle,
                Kind = FilterKind.Bool,
                Options = new[] { new FilterOption("1", ruTitle) }
            };
        }

        if (param.Range != null)
        {
            return new FilterSpec
            {
                Key = param.Name!,
                UrlName = urlName,
                Title = ruTitle,
                Kind = FilterKind.Range,
                RangeLower = param.Range.Lower,
                RangeUpper = param.Range.Upper
            };
        }

        var options = (param.Values ?? new List<KufarParameterValueDto>())
            .Select(v =>
            {
                bool has = FilterSpecFactoryHelpers.TryValue(v.Value, out var value);
                string? label = v.Labels?.Ru?.Trim();
                return (Has: has, Value: value, Label: label);
            })
            .Where(x => x.Has && !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new FilterOption(x.Value, string.IsNullOrWhiteSpace(x.Label) ? x.Value : x.Label!))
            .ToList();

        if (options.Count == 0)
        {
            // свободный ввод
            return new FilterSpec
            {
                Key = param.Name!,
                UrlName = urlName,
                Title = ruTitle,
                Kind = FilterKind.Text
            };
        }

        bool multi = param.Multi || ContainsIgnoreCase(param.UIComponent?.Type, "multiselect");
        return new FilterSpec
        {
            Key = param.Name!,
            UrlName = urlName,
            Title = ruTitle,
            Kind = multi ? FilterKind.Multi : FilterKind.Single,
            Multi = multi,
            Options = options
        };
    }

    private static bool ContainsIgnoreCase(string? source, string fragment)
    {
        return source != null &&
               source.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class FilterSpecFactoryHelpers
{
    public static bool TryValue(object? raw, out string value)
    {
        if (raw == null)
        {
            value = "";
            return false;
        }

        if (raw is bool b)
        {
            value = b ? "1" : "0";
            return true;
        }

        var s = raw.ToString()?.Trim() ?? "";
        value = s;
        return s.Length > 0;
    }
}
