using System.Text.Json.Serialization;

namespace WebApplicationAPI.Models.Ad
{
    public class Pagination
    {
        [JsonPropertyName("pages")]
        public List<Page> Pages { get; set; }
    }
}