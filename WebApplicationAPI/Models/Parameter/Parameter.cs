using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class Parameter
    {
        [JsonPropertyName("variation_id")]
        public int VariationId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("url_name")]
        public string UrlName { get; set; }

        [JsonPropertyName("required")]
        public bool Required { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("meta")]
        public string Meta { get; set; }

        [JsonPropertyName("multi")]
        public bool Multi { get; set; }

        [JsonPropertyName("external_values_url")]
        public string ExternalValuesUrl { get; set; }

        [JsonPropertyName("range")]
        public Range? Range { get; set; }

        [JsonPropertyName("hint")]
        public Hint Hint { get; set; }

        [JsonPropertyName("labels")]
        public Labels Labels { get; set; }

        [JsonPropertyName("is_type")]
        public bool IsType { get; set; }

        [JsonPropertyName("values")]
        public List<ParameterValue> Values { get; set; }

    }
}
