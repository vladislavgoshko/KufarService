using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Category
{
    public class Subcategory
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("parent")]
        public string Parent { get; set; }

        [JsonPropertyName("order")]
        public string Order { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("name_ru")]
        public string Name_ru { get; set; }

        [JsonPropertyName("name_by")]
        public string Name_by { get; set; }

        [JsonPropertyName("redirect")]
        public string? Redirect { get; set; }
    }
}