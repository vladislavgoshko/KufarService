using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Category
{
    public class CategoryData
    {
        [JsonPropertyName("categories")]
        public List<Category> Categories { get; set; }
    }
}
