using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class CategoryWithParameters
    {
        [JsonPropertyName("category_name")]
        public string CategoryName { get; set; }

        [JsonPropertyName("rule")]
        public Dictionary<string, string> Rule { get; set; }

        [JsonPropertyName("parameters")]
        public List<Parameter> Parameters { get; set; }
    }
}
