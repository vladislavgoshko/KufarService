using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Ad
{
    public class PaidServices
    {
        [JsonPropertyName("halva")]
        public bool Halva { get; set; }

        [JsonPropertyName("highlight")]
        public bool Highlight { get; set; }

        [JsonPropertyName("polepos")]
        public bool Polepos { get; set; }

        [JsonPropertyName("ribbons")]
        public object Ribbons { get; set; }
    }
}