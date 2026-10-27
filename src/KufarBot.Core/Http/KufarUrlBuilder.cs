using System;
using System.Collections.Generic;
using System.Linq;
using KufarBot.Core.Models;

namespace KufarBot.Core.Http;

public static class KufarUrlBuilder
{
    public const string DefaultBaseUrl = "https://api.kufar.by/search-api/v2/search/rendered-paginated";
    public const string CategoryTreeUrl = "https://api.kufar.by/category-tree/v1/category_tree";
    public const string ImageBaseUrl = "https://rms.kufar.by/v1/list_thumbs_2x";
    private const long DefaultMaxPriceKopecks = 999999999L;

    public static string BuildSearchUrl(KufarSearchCriteria criteria, string baseUrl = DefaultBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var queryParams = new List<string>
        {
            $"cur={Uri.EscapeDataString(string.IsNullOrWhiteSpace(criteria.Currency) ? "BYN" : criteria.Currency)}",
            $"sort={Uri.EscapeDataString(string.IsNullOrWhiteSpace(criteria.Sort) ? "lst.d" : criteria.Sort)}",
            $"size={Math.Clamp(criteria.Size, 1, 200)}"
        };

        if (criteria.CategoryId.HasValue)
        {
            queryParams.Add($"cat={criteria.CategoryId.Value}");
        }

        if (criteria.AreaId.HasValue)
        {
            queryParams.Add($"ar={criteria.AreaId.Value}");
        }

        if (criteria.RegionId.HasValue)
        {
            queryParams.Add($"rgn={criteria.RegionId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(criteria.Query))
        {
            queryParams.Add($"query={Uri.EscapeDataString(criteria.Query)}");
        }

        if (criteria.UrlFilters != null)
        {
            foreach (var pair in criteria.UrlFilters.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                queryParams.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
            }
        }

        if (criteria.MinPriceByn.HasValue || criteria.MaxPriceByn.HasValue)
        {
            long minKopecks = criteria.MinPriceByn.HasValue
                ? Math.Max(0L, (long)Math.Round(criteria.MinPriceByn.Value * 100m, MidpointRounding.AwayFromZero))
                : 0L;

            long maxKopecks = criteria.MaxPriceByn.HasValue
                ? Math.Max(minKopecks, (long)Math.Round(criteria.MaxPriceByn.Value * 100m, MidpointRounding.AwayFromZero))
                : DefaultMaxPriceKopecks;

            queryParams.Add($"prc=r:{minKopecks},{maxKopecks}");
        }

        if (!string.IsNullOrWhiteSpace(criteria.Cursor))
        {
            queryParams.Add($"cursor={Uri.EscapeDataString(criteria.Cursor)}");
        }

        return $"{baseUrl}?{string.Join("&", queryParams)}";
    }

    public static string? FormatImageUrl(string? rawPath, string imageBaseUrl = ImageBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var trimmed = rawPath.Trim().TrimStart('/');
        return $"{imageBaseUrl.TrimEnd('/')}/{trimmed}";
    }
}
