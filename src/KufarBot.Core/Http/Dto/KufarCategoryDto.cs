using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KufarBot.Core.Http.Dto;

public class KufarCategoryTreeResponseDto
{
    [JsonPropertyName("categories")]
    public List<KufarCategoryDto>? Categories { get; set; }
}

public class KufarCategoryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("order")]
    public string? Order { get; set; }

    [JsonPropertyName("subcategories")]
    public List<KufarSubcategoryDto>? Subcategories { get; set; }
}

public class KufarSubcategoryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("order")]
    public string? Order { get; set; }
}
