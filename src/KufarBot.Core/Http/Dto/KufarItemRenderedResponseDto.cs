using System.Text.Json.Serialization;

namespace KufarBot.Core.Http.Dto;

/// <summary>Response of search-api/v2/item/{id}/rendered — the ad details wrapped in "result".</summary>
public class KufarItemRenderedResponseDto
{
    [JsonPropertyName("result")]
    public KufarAdDto? Result { get; set; }
}
