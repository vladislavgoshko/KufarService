using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace KufarBot.Core.Http.Dto;

public class KufarPaginationDto
{
    [JsonPropertyName("pages")]
    public List<KufarPageDto>? Pages { get; set; }

    public string? NextToken => Pages?.FirstOrDefault(p => p.Label == "next")?.Token;
}

public class KufarPageDto
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("num")]
    public int? Num { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
