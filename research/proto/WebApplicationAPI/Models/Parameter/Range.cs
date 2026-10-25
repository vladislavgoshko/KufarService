using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class Range
    {
        [JsonPropertyName("default_lower")]
        public int DefaultLower { get; set; }

        [JsonPropertyName("default_upper")]
        public int DefaultUpper { get; set; }

        [JsonPropertyName("lower")]
        public int Lower { get; set; }

        [JsonPropertyName("step")]
        public int Step { get; set; }

        [JsonPropertyName("upper")]
        public int Upper { get; set; }
    }
}
