using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class LabelText
    {
        [JsonPropertyName("ru")]
        public string Ru { get; set; }

        [JsonPropertyName("by")]
        public string By { get; set; }
    }
}
