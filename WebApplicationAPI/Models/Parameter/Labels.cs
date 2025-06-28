using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class Labels
    {
        [JsonPropertyName("name")]
        public LabelText Name { get; set; }
    }
}
