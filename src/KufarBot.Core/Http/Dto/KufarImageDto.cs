using System.Text.Json.Serialization;

namespace KufarBot.Core.Http.Dto;

public class KufarImageDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("media_storage")]
    public string? MediaStorage { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("yams_storage")]
    public bool? YamsStorage { get; set; }
}
