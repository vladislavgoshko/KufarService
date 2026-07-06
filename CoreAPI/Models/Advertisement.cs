using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CoreAPI.Models
{
    public class Advertisement
    {
        [JsonPropertyName("ad_id")]
        public long Id { get; set; }

        [JsonPropertyName("subject")]
        public string Title { get; set; }

        [JsonPropertyName("body")]
        public string Description { get; set; }

        [JsonPropertyName("body_short")]
        public string DescriptionShort { get; set; }

        [JsonPropertyName("price")]
        public decimal? Price { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; }

        [JsonPropertyName("region")]
        public string Location { get; set; }

        [JsonPropertyName("list_time")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("ad_link")]
        public string Url { get; set; }

        [JsonPropertyName("images")]
        public List<Image> Images { get; set; }

        public string ImageUrl
        {
            get => Images?.FirstOrDefault()?.Url ?? "";
        }

        public override string ToString()
        {
            var price = Price.HasValue ? $"{Price} {Currency}" : "По договоренности";
            return $"📢 {Title}\n💰 {price}\n📍 {Location}\n🔗 {Url}";
        }

        public class Image
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("full")]
            public FullImage Full { get; set; }

            [JsonPropertyName("thumbnail")]
            public ThumbnailImage Thumbnail { get; set; }

            public class FullImage
            {
                [JsonPropertyName("url")]
                public string Url { get; set; }
            }

            public class ThumbnailImage
            {
                [JsonPropertyName("url")]
                public string Url { get; set; }
            }

            public string Url => Full?.Url ?? Thumbnail?.Url ?? "";
        }
    }

    public class KufarResponse
    {
        [JsonPropertyName("ads")]
        public List<Advertisement> Ads { get; set; }

        [JsonPropertyName("pagination")]
        public Pagination Pagination { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    public class Pagination
    {
        [JsonPropertyName("cursor")]
        public string Cursor { get; set; }
    }
}
