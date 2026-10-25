using System.Collections.Generic;
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
}

public class KufarParametersContainerDto
{
    [JsonPropertyName("dispatch_type")]
    public string? DispatchType { get; set; }

    [JsonPropertyName("deduplication_key")]
    public string? DeduplicationKey { get; set; }

    [JsonPropertyName("refs")]
    public Dictionary<string, KufarParameterDto>? Refs { get; set; }
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

    [JsonPropertyName("ui_component")]
    public KufarUIComponentDto? UIComponent { get; set; }
}

public class KufarRangeDto
{
    [JsonPropertyName("lower")]
    public int Lower { get; set; }

    [JsonPropertyName("step")]
    public int Step { get; set; }

    [JsonPropertyName("upper")]
    public int Upper { get; set; }
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
