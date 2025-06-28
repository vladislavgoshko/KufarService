using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class ParametersContainer
    {
        [JsonPropertyName("dispatch_type")]
        public string DispatchType { get; set; }

        [JsonPropertyName("deduplication_key")]
        public string DedupicationKey { get; set; } // Может быть null, поэтому string

        [JsonPropertyName("refs")]
        public Dictionary<string, Parameter> Refs { get; set; } // Справочник всех параметров
        
        [JsonPropertyName("rules")]
        public List<RuleInfo> Rules { get; set; } // Список правил
    }
}
