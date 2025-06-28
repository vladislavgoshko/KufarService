using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class Hint
    {
        [JsonPropertyName("by")]
        public string By { get; set; }

        [JsonPropertyName("ru")]
        public string Ru { get; set; }

    }
}