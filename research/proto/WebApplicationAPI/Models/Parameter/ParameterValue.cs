using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class ParameterValue
    {
        [JsonPropertyName("value")]
        public string Value { get; set; }

        [JsonPropertyName("labels")]
        public LabelText Labels { get; set; }

        [JsonPropertyName("sub_parameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Parameter>? SubParameters { get; set; }
    }
}
