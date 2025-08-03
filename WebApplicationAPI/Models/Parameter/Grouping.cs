using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class Grouping
    {
        [JsonPropertyName("group_id")]
        public int GroupId { get; set; }

        [JsonPropertyName("group_order")]
        public int GroupOrder { get; set; }

        [JsonPropertyName("has_name")]
        public bool HasName { get; set; }

        [JsonPropertyName("label_by")]
        public string LabelBy { get; set; }

        [JsonPropertyName("label_ru")]
        public string LabelRu { get; set; }

        [JsonPropertyName("parameter_order")]
        public int ParameterOrder { get; set; }
    }
}
