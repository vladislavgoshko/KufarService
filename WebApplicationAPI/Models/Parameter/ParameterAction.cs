using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class ParameterAction
    {
        [JsonPropertyName("action_id")]
        public int ActionId { get; set; }

        [JsonPropertyName("args")]
        public int? Args { get; set; }

        [JsonPropertyName("depends_on")]
        public int? DependsOn { get; set; }

        // Возможные значения: "v_coordinates", "m_error", "v_boolean", "v_region", "v_address_tags", "v_string", "m_address_tags", "v_length", "v_integer", "v_single", "v_currency", "v_price", "v_required", "v_area", "m_delete"
        [JsonPropertyName("name")]
        public string Name { get; set; }

        // Возможные значения: "validator", "mutator"
        [JsonPropertyName("type")]
        public string Type { get; set; }
    }
}
