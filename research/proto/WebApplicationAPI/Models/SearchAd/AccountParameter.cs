using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Ad
{
    public class AccountParameter
    {
        [JsonPropertyName("pl")]
        public string Pl { get; set; }

        [JsonPropertyName("vl")]
        public string Vl { get; set; }

        [JsonPropertyName("p")]
        public string P { get; set; }

        [JsonPropertyName("v")]
        public string V { get; set; }

        [JsonPropertyName("pu")]
        public string Pu { get; set; }
    }
}