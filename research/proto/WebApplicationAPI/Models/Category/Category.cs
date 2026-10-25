using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Category
{
    public class Category
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("name_ru")]
        public string Name_ru { get; set; }

        [JsonPropertyName("name_by")]
        public string Name_by { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("order")]
        public string Order { get; set; }

        [JsonPropertyName("subcategories")]
        public List<Subcategory> Subcategories { get; set; }
    }
}