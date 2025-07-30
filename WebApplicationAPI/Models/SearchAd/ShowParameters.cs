using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Ad
{
    public class ShowParameters
    {
        [JsonPropertyName("show_call")]
        public bool ShowCall { get; set; }

        [JsonPropertyName("show_chat")]
        public bool ShowChat { get; set; }

        [JsonPropertyName("show_import_link")]
        public bool ShowImportLink { get; set; }

        [JsonPropertyName("show_web_shop_link")]
        public bool ShowWebShopLink { get; set; }
    }
}