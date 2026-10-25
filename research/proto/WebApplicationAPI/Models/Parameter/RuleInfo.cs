using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class RuleInfo
    {
        [JsonPropertyName("rule")]
        public Dictionary<string, string> Rule { get; set; }

        [JsonPropertyName("refs")]
        public List<string> Refs { get; set; }
    }
}
