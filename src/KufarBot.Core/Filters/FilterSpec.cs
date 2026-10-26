using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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

    /// <summary>Search tool toggles that are no attributes of the ad itself — cannot be checked client-side.</summary>
    private static readonly HashSet<string> NotAdLevelParameterNames = new()
    {
        "only_with_photo"
    };

    /// <summary>
    /// Flattens the Kufar dispatch into FilterSpecs (whole category, all scenarios). Used by tests
    /// and kept as a compatibility view; the bot UI uses <see cref="FromDispatchForSubcategory"/>.
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
            if (string.IsNullOrWhiteSpace(param.Name) || BuiltInParameterNames.Contains(param.Name) ||
                NotAdLevelParameterNames.Contains(param.Name))
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

    /// <summary>
    /// Builds the filter list shown in the wizard for one subcategory.
    /// The dispatch is fetched for the whole category (parent = category id), so here the
    /// parameters are selected by the scenario rules:
    ///   • empty rule  — base parameters of every subcategory in the category;
    ///   • {"category": "&lt;subcategoryId&gt;"} — parameters specific to this subcategory;
    ///   • {"category": "...", "&lt;type param&gt;": value} — conditional sub-subcategory parameters (skipped for now);
    ///   • {"region": ...} / {"area": ...} — region-scenario parameters (skipped).
    /// Results are sorted by metadata.parameters_order (UI order on Kufar).
    /// </summary>
    public static List<FilterSpec> FromDispatchForSubcategory(KufarParametersResponseDto? dispatch, int subcategoryId)
    {
        var container = dispatch?.Metadata?.Parameters;
        if (container?.Refs == null)
        {
            return new List<FilterSpec>();
        }

        string subcategoryIdText = subcategoryId.ToString(CultureInfo.InvariantCulture);
        var selectedRefIds = new List<string>();

        var rules = container.Rules;
        if (rules is { Count: > 0 })
        {
            foreach (var binding in rules)
            {
                var rule = binding.Rule;
                if (rule == null || rule.Count == 0)
                {
                    // base scenario: parameters shared by every subcategory
                    selectedRefIds.AddRange(binding.Refs ?? new List<string>());
                }
                else if (rule.Count == 1 &&
                         rule.TryGetValue("category", out JsonElement categoryValue) &&
                         RuleValueAsText(categoryValue) == subcategoryIdText)
                {
                    selectedRefIds.AddRange(binding.Refs ?? new List<string>());
                }
            }
        }
        else
        {
            // response without scenario rules — expose everything (compatibility)
            selectedRefIds.AddRange(container.Refs.Keys);
        }

        var orderIndex = BuildOrderIndex(dispatch!.Metadata?.ParametersOrder);

        var picked = new List<(int VariationId, FilterSpec Spec)>();
        var seenNames = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (string refId in selectedRefIds.Distinct())
        {
            if (!container.Refs.TryGetValue(refId, out KufarParameterDto? param) ||
                string.IsNullOrWhiteSpace(param.Name) ||
                BuiltInParameterNames.Contains(param.Name) ||
                NotAdLevelParameterNames.Contains(param.Name) ||
                !seenNames.Add(param.Name))
            {
                continue;
            }

            var spec = BuildSpec(param);
            if (spec != null)
            {
                picked.Add((param.VariationId, spec));
            }
        }

        return picked
            .OrderBy(p => orderIndex.TryGetValue(p.VariationId, out int index) ? index : int.MaxValue)
            .Select(p => p.Spec)
            .ToList();
    }

    private static Dictionary<int, int> BuildOrderIndex(KufarParametersOrderDto? order)
    {
        var index = new Dictionary<int, int>();
        if (order?.Refs == null)
        {
            return index;
        }

        foreach (var list in order.Refs.Values)
        {
            foreach (string rawValue in list ?? new List<string>())
            {
                if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int variationId) &&
                    !index.ContainsKey(variationId))
                {
                    index[variationId] = index.Count;
                }
            }
        }

        return index;
    }

    private static string? RuleValueAsText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => element.ToString()
        };
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
