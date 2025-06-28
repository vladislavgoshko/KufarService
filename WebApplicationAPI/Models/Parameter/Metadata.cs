using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Parameter
{
    public class Metadata
    {
        [JsonPropertyName("parameters")]
        public ParametersContainer Parameters { get; set; }

        [JsonPropertyName("parameters_order")] // Теперь это корректное свойство
        public ParametersOrderContainer ParametersOrder { get; set; }
    }
}
