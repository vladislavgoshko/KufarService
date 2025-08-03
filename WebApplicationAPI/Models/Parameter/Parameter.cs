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

        // "bool", "string", "int"
        [JsonPropertyName("type")]
        public string Type { get; set; }

        // "set_free", "set", "free"
        [JsonPropertyName("meta")]
        public string Meta { get; set; }

        [JsonPropertyName("multi")]
        public bool Multi { get; set; }

        //null
        [JsonPropertyName("external_values_url")]
        public string ExternalValuesUrl { get; set; }

        [JsonPropertyName("range")]
        public Range? Range { get; set; }

        //null
        [JsonPropertyName("hint")]
        public Hint Hint { get; set; }

        [JsonPropertyName("labels")]
        public Labels Labels { get; set; }

        [JsonPropertyName("is_type")]
        public bool IsType { get; set; }

        [JsonPropertyName("values")]
        public List<ParameterValue> Values { get; set; }

        [JsonPropertyName("ui_component")]
        public UIComponent UIComponent { get; set; }

        // добавленные свойства
        [JsonPropertyName("actions")]
        public List<ParameterAction> Actions { get; set; }

        [JsonPropertyName("grouping")]
        public Grouping Grouping { get; set; }
    }
}
