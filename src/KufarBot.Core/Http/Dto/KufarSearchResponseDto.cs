using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KufarBot.Core.Http.Dto;

public class KufarSearchResponseDto
{
    [JsonPropertyName("ads")]
    public List<KufarAdDto>? Ads { get; set; }

    [JsonPropertyName("pagination")]
    public KufarPaginationDto? Pagination { get; set; }

    [JsonPropertyName("total")]
    public int? Total { get; set; }
}
