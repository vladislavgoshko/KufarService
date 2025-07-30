using System.Text.Json.Serialization;
using WebApplicationAPI.Tools;

namespace WebApplicationAPI.Models.Ad
{
    public class AdParameter
    {
        [JsonPropertyName("pl")]
        public string Pl { get; set; }

        [JsonPropertyName("vl")]
        [JsonConverter(typeof(AnyToStringConverter))]
        public string Vl { get; set; }

        [JsonPropertyName("p")]
        public string P { get; set; }

        [JsonPropertyName("v")]
        [JsonConverter(typeof(AnyToStringConverter))]
        public string V { get; set; }

        [JsonPropertyName("pu")]
        public string Pu { get; set; }
    }
}