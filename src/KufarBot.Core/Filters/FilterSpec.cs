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
    /// <summary>Built-in scenario keys handled by the bot as first-class draft settings (city, prices, subcategory).</summary>
    private static readonly HashSet<string> BuiltInParameterNames = new()
    {
        "region",
        "area",
        "category"
    };

    /// <summary>
    /// Not shown as filters: built-in scenario keys, ad-posting/business form fields,
    /// paid-service flags and other search-tool toggles. None of them can be matched
    /// client-side against the ad parameter snapshot, and none are search filters on the site.
    /// </summary>
    private static readonly HashSet<string> NotAdLevelParameterNames = new()
    {
        // search-tool toggles (handled by bot's own filters / not present on ads)
        "only_with_photo", "only_with_content_video", "photo_3d", "content_video", "ribbons", "name",

        // price/currency/geo are bot-level (min/max price, city)
        "price", "currency", "region", "area", "category", "parent_category",

        // company posting form / business registry
        "company_ad", "company_address", "company_number", "shop_address", "web_shop_link",
        "import_link", "contact_person", "vat_number", "egr_number", "egr_date", "egr_authority",
        "commercial_register_number", "commercial_register_date", "domestic_register_number",
        "domestic_register_date",

        // paid-services / cross-posting service flags
        "regular_price", "delivery_pro", "installment_pro", "sell_out",
        "multiregion_enabled", "multiregion_regions", "users_synonyms"
    };

    /// <summary>
    /// Service boolean flags that ARE stored on ads and filterable on the site
    /// (observed in ad_parameters of real search results). Every other bool parameter
    /// (posting form toggles) is skipped.
    /// </summary>
    private static readonly HashSet<string> BoolFilterWhitelist = new()
    {
        "possible_exchange", "halva", "delivery_enabled", "safedeal_enabled", "b2c_availability"
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
    /// Builds FilterSpecs for every subcategory of the category in one pass.
    /// Useful together with the client-side cache: одим запросом получает параметры всех подкатегорий.
    /// Keys = subcategory ids referenced by single-key rules {"category": "..."}.
    /// Empty rules list (or null) → пустой словарь (вызовет fallback на FromDispatchForSubcategory).
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<FilterSpec>> FromDispatchByCategory(KufarParametersResponseDto? dispatch)
    {
        var container = dispatch?.Metadata?.Parameters;
        if (container?.Refs == null || container.Rules is not { Count: > 0 })
        {
            return new Dictionary<int, IReadOnlyList<FilterSpec>>();
        }

        var orderIndex = BuildOrderIndex(dispatch!.Metadata?.ParametersOrder);

        var baseRefIds = new List<string>();
        var subcatRefs = new Dictionary<int, List<string>>();

        foreach (var binding in container.Rules)
        {
            var rule = binding.Rule;
            if (rule == null || rule.Count == 0)
            {
                // base scenario: parameters shared by every subcategory
                baseRefIds.AddRange(binding.Refs ?? new List<string>());
            }
            else if (rule.Count == 1 &&
                     rule.TryGetValue("category", out JsonElement categoryValue) &&
                     int.TryParse(RuleValueAsText(categoryValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out int subcategoryId))
            {
                if (!subcatRefs.TryGetValue(subcategoryId, out var list))
                {
                    list = new List<string>();
                    subcatRefs[subcategoryId] = list;
                }

                list.AddRange(binding.Refs ?? new List<string>());
            }
            // other rules (region/area-bound, type-conditional consequences) are skipped for now
        }

        var result = new Dictionary<int, IReadOnlyList<FilterSpec>>();
        foreach (var (subcategoryId, refs) in subcatRefs)
        {
            result[subcategoryId] = BuildSpecsForRefSet(baseRefIds.Concat(refs), container.Refs, orderIndex);
        }

        return result;
    }

    /// <summary>
    /// Filter parameters for one subcategory. The dispatch must be requested for the whole
    /// category (parent = category id, e.g. 16000) — subcategory-level requests return 404.
    /// </summary>
    public static List<FilterSpec> FromDispatchForSubcategory(KufarParametersResponseDto? dispatch, int subcategoryId)
    {
        var map = FromDispatchByCategory(dispatch);
        if (map.TryGetValue(subcategoryId, out IReadOnlyList<FilterSpec>? specs))
        {
            return specs.ToList();
        }

        var container = dispatch?.Metadata?.Parameters;
        if (container?.Refs == null)
        {
            return new List<FilterSpec>();
        }

        var orderIndex = BuildOrderIndex(dispatch!.Metadata?.ParametersOrder);

        if (container.Rules is { Count: > 0 })
        {
            // the subcategory has no own rule — base parameters only
            var baseRefIds = new List<string>();
            foreach (var binding in container.Rules)
            {
                if (binding.Rule == null || binding.Rule.Count == 0)
                {
                    baseRefIds.AddRange(binding.Refs ?? new List<string>());
                }
            }

            return BuildSpecsForRefSet(baseRefIds, container.Refs, orderIndex);
        }

        // compatibility: response without scenario rules — expose everything
        return BuildSpecsForRefSet(container.Refs.Keys, container.Refs, orderIndex);
    }

    private static List<FilterSpec> BuildSpecsForRefSet(
        IEnumerable<string> refIds,
        IReadOnlyDictionary<string, KufarParameterDto> allRefs,
        Dictionary<int, int> orderIndex)
    {
        var picked = new List<(int VariationId, FilterSpec Spec)>();
        var seenNames = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (string refId in refIds.Distinct())
        {
            if (!allRefs.TryGetValue(refId, out KufarParameterDto? param) ||
                string.IsNullOrWhiteSpace(param.Name) ||
                BuiltInParameterNames.Contains(param.Name) ||
                NotAdLevelParameterNames.Contains(param.Name) ||
                !seenNames.Add(param.Name))
            {
                continue;
            }

            var spec = BuildSpec(param);
            if (spec == null)
            {
                continue;
            }

            // bool toggles that don't exist as ad attributes would filter everything away
            if (spec.Kind == FilterKind.Bool && !BoolFilterWhitelist.Contains(spec.Key))
            {
                continue;
            }

            picked.Add((param.VariationId, spec));
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
