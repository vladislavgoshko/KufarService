using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Ad
{
    public class Image
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("media_storage")]
        public string MediaStorage { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; }
        
        [JsonPropertyName("yams_storage")]
        public bool YamsStorage { get; set; }
    }
}