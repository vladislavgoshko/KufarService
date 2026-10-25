using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KufarBot.Core.Http.Dto;

public class KufarAdDto
{
    [JsonPropertyName("ad_id")]
    public long AdId { get; set; }

    [JsonPropertyName("ad_link")]
    public string? AdLink { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("body_short")]
    public string? BodyShort { get; set; }

    [JsonPropertyName("price_byn")]
    public string? PriceByn { get; set; }

    [JsonPropertyName("price_usd")]
    public string? PriceUsd { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("list_time")]
    public string? ListTime { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("remuneration_type")]
    public string? RemunerationType { get; set; }

    [JsonPropertyName("company_ad")]
    public bool? CompanyAd { get; set; }

    [JsonPropertyName("ad_parameters")]
    public List<KufarAdParameterDto>? AdParameters { get; set; }

    [JsonPropertyName("images")]
    public List<KufarImageDto>? Images { get; set; }
}
