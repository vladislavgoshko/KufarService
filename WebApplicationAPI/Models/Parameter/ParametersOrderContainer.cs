using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class ParametersOrderContainer
    {
        [JsonPropertyName("dispatch_type")]
        public string DispatchType { get; set; }

        [JsonPropertyName("deduplication_key")]
        public string DedupicationKey { get; set; }

        [JsonPropertyName("refs")]
        public Dictionary<string, List<string>> Refs { get; set; }
    }
}