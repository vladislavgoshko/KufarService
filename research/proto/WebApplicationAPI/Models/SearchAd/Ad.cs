using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Ad
{
    public class Ad
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; }

        [JsonPropertyName("account_parameters")]
        public List<AccountParameter> AccountParameters { get; set; }

        [JsonPropertyName("ad_id")]
        public long AdId { get; set; }

        [JsonPropertyName("ad_link")]
        public string AdLink { get; set; }

        [JsonPropertyName("ad_parameters")]
        public List<AdParameter> AdParameters { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; }

        [JsonPropertyName("body_short")]
        public string BodyShort { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("company_ad")]
        public bool CompanyAd { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; }

        [JsonPropertyName("images")]
        public List<Image> Images { get; set; }

        [JsonPropertyName("is_mine")]
        public bool IsMine { get; set; }

        [JsonPropertyName("list_id")]
        public long ListId { get; set; }

        [JsonPropertyName("list_time")]
        public DateTime ListTime { get; set; }

        [JsonPropertyName("message_id")]
        public string MessageId { get; set; }

        [JsonPropertyName("paid_services")]
        public PaidServices PaidServices { get; set; }

        [JsonPropertyName("phone_hidden")]
        public bool PhoneHidden { get; set; }

        [JsonPropertyName("price_byn")]
        public string PriceByn { get; set; }

        [JsonPropertyName("price_usd")]
        public string PriceUsd { get; set; }

        [JsonPropertyName("remuneration_type")]
        public string RemunerationType { get; set; }

        [JsonPropertyName("show_parameters")]
        public ShowParameters ShowParameters { get; set; }

        [JsonPropertyName("subject")]
        public string Subject { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }
}