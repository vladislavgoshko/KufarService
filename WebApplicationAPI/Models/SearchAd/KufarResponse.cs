using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Ad
{
    public class KufarResponse
    {
        [JsonPropertyName("ads")]
        public List<Ad>? Ads { get; set; }

        [JsonPropertyName("pagination")]
        public Pagination Pagination { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

}
