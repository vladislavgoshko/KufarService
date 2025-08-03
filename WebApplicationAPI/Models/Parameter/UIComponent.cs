using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class UIComponent
    {
        // Возможные значения: select, input, multiselect_chips_or, radio_button, multiselect_or, input_range, checkbox, select_range, multiselect_and, null.
        [JsonPropertyName("fallback_type")]
        public string FallbackType { get; set; }

        /// <summary>
        // Возможные значения: select, input, multiselect_chips_or, radio_button, multiselect_or, input_range, checkbox, select_search, select_range, multiselect_chips_and, select_chips, new_checkbox, multiselect_and, null.
        [JsonPropertyName("type")]
        public string Type { get; set; }
    }
}
