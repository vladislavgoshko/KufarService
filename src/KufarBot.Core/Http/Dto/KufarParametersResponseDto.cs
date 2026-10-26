using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KufarBot.Core.Http.Dto;

/// <summary>
/// Shape of GET https://api.kufar.by/taxonomy-proxy/v1/dispatch?routing=web_generalist&application=ad_listing&platform=web&taxonomy-version=1&parent={categoryId}
/// (discovered in research/notes and validated against research/data/api_data/parameters.json).
/// </summary>
public class KufarParametersResponseDto
{
    [JsonPropertyName("metadata")]
    public KufarParametersMetadataDto? Metadata { get; set; }
}

public class KufarParametersMetadataDto
{
    [JsonPropertyName("parameters")]
    public KufarParametersContainerDto? Parameters { get; set; }

    /// <summary>Global UI order of parameter variations (metadata.parameters_order).</summary>
    [JsonPropertyName("parameters_order")]
    public KufarParametersOrderDto? ParametersOrder { get; set; }
}

public class KufarParametersContainerDto
{
    [JsonPropertyName("dispatch_type")]
    public string? DispatchType { get; set; }

    [JsonPropertyName("deduplication_key")]
    public string? DeduplicationKey { get; set; }

    [JsonPropertyName("refs")]
    public Dictionary<string, KufarParameterDto>? Refs { get; set; }

    /// <summary>
    /// Scenario bindings: empty rule = base parameters of the category,
    /// {"category":"16010"} = subcategory-specific, {"category":"16010", "&lt;param&gt;":"5"} =
    /// conditional parameters of an ad type (sub-subcategory), {"region":"7"} = region-specific.
    /// </summary>
    [JsonPropertyName("rules")]
    public List<KufarParameterRuleBindingDto>? Rules { get; set; }
}

public class KufarParameterRuleBindingDto
{
    /// <summary>Condition map; empty object means the base scenario.</summary>
    [JsonPropertyName("rule")]
    public Dictionary<string, JsonElement>? Rule { get; set; }

    [JsonPropertyName("refs")]
    public List<string>? Refs { get; set; }
}

public class KufarParametersOrderDto
{
    // single scenario key ("0") -> ordered variation ids
    [JsonPropertyName("refs")]
    public Dictionary<string, List<string>>? Refs { get; set; }
}

public class KufarParameterDto
{
    [JsonPropertyName("variation_id")]
    public int VariationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url_name")]
    public string? UrlName { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    // "bool" | "string" | "int"
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    // "set" | "free" | ...
    [JsonPropertyName("meta")]
    public string? Meta { get; set; }

    [JsonPropertyName("multi")]
    public bool Multi { get; set; }

    [JsonPropertyName("range")]
    public KufarRangeDto? Range { get; set; }

    [JsonPropertyName("labels")]
    public KufarParameterLabelsDto? Labels { get; set; }

    [JsonPropertyName("values")]
    public List<KufarParameterValueDto>? Values { get; set; }

    [JsonPropertyName("min_taxonomy_version")]
    public int MinTaxonomyVersion { get; set; }

    [JsonPropertyName("is_type")]
    public bool IsType { get; set; }

    /// <summary>Localized hint text; on the wire it can be an object {ru, by} — bound as raw JSON.</summary>
    [JsonPropertyName("hint")]
    public JsonElement? Hint { get; set; }

    /// <summary>May be string, object or absent depending on the parameter — bound as raw JSON.</summary>
    [JsonPropertyName("image_url")]
    public JsonElement? ImageUrl { get; set; }

    [JsonPropertyName("external_values_url")]
    public string? ExternalValuesUrl { get; set; }

    [JsonPropertyName("ui_component")]
    public KufarUIComponentDto? UIComponent { get; set; }
}

public class KufarRangeDto
{
    [JsonPropertyName("lower")]
    public decimal Lower { get; set; }

    [JsonPropertyName("step")]
    public decimal Step { get; set; }

    [JsonPropertyName("upper")]
    public decimal Upper { get; set; }
}

public class KufarUIComponentDto
{
    // select, input, multiselect_or, multiselect_and, checkbox, input_range, select_range, ...
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("fallback_type")]
    public string? FallbackType { get; set; }
}

public class KufarParameterLabelsDto
{
    [JsonPropertyName("name")]
    public KufarLabelTextDto? Name { get; set; }
}

public class KufarParameterValueDto
{
    // untyped on the wire: number, string or bool  ->  keep as JsonElement
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("labels")]
    public KufarLabelTextDto? Labels { get; set; }
}

public class KufarLabelTextDto
{
    [JsonPropertyName("ru")]
    public string? Ru { get; set; }

    [JsonPropertyName("by")]
    public string? By { get; set; }
}
