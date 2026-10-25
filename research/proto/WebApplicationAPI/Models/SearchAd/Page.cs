using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Ad
{
    public class Page
    {
        [JsonPropertyName("label")]
        public string Label { get; set; }

        [JsonPropertyName("num")]
        public int Num { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; }
    }
}