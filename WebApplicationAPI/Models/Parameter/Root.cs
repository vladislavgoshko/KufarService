using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class Root
    {
        [JsonPropertyName("metadata")]
        public Metadata Metadata { get; set; }
    }
}
