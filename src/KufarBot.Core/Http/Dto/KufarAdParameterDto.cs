using System.Text.Json.Serialization;

namespace KufarBot.Core.Http.Dto;

public class KufarAdParameterDto
{
    [JsonPropertyName("p")]
    public string? ParameterName { get; set; }

    [JsonPropertyName("pl")]
    public string? ParameterLabel { get; set; }

    [JsonPropertyName("pu")]
    public string? ParameterUrl { get; set; }

    [JsonPropertyName("vl")]
    public object? ValueLabel { get; set; }

    [JsonPropertyName("v")]
    public object? Value { get; set; }
}
