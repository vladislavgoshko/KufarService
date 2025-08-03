using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class ParametersContainer
    {
        //"many",
        //"one"
        [JsonPropertyName("dispatch_type")]
        public string DispatchType { get; set; }

        //null,
        //"name"
        [JsonPropertyName("deduplication_key")]
        public string DedupicationKey { get; set; }

        [JsonPropertyName("refs")]
        public Dictionary<string, Parameter> Refs { get; set; }
        
        [JsonPropertyName("rules")]
        public List<RuleInfo> Rules { get; set; }
    }
}
