using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Filters;
using KufarBot.Core.Http.Dto;
using KufarBot.Core.Models;

namespace KufarBot.Core.Http;

public class KufarApiClient : IKufarApiClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public KufarApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<KufarSearchResult> SearchAsync(KufarSearchCriteria criteria, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        string url = KufarUrlBuilder.BuildSearchUrl(criteria);
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var searchDto = await JsonSerializer.DeserializeAsync<KufarSearchResponseDto>(stream, JsonOptions, ct);

        var normalizedAds = KufarAdNormalizer.NormalizeMany(searchDto?.Ads);
        string? nextCursor = searchDto?.Pagination?.NextToken;
        int? total = searchDto?.Total;

        return new KufarSearchResult(normalizedAds, nextCursor, total);
    }

    public async Task<IReadOnlyList<KufarCategory>> GetCategoryTreeAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(KufarUrlBuilder.CategoryTreeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var treeDto = await JsonSerializer.DeserializeAsync<KufarCategoryTreeResponseDto>(stream, JsonOptions, ct);

        if (treeDto?.Categories == null)
        {
            return Array.Empty<KufarCategory>();
        }

        var categories = new List<KufarCategory>();
        foreach (var catDto in treeDto.Categories)
        {
            if (!int.TryParse(catDto.Id, out int catId))
            {
                continue;
            }

            int catOrder = int.TryParse(catDto.Order, out int orderVal) ? orderVal : int.MaxValue;
            var subcategories = new List<KufarSubcategory>();

            if (catDto.Subcategories != null)
            {
                foreach (var subDto in catDto.Subcategories)
                {
                    if (int.TryParse(subDto.Id, out int subId))
                    {
                        int subOrder = int.TryParse(subDto.Order, out int subOrderVal) ? subOrderVal : int.MaxValue;
                        subcategories.Add(new KufarSubcategory(subId, catId, subDto.Name ?? "Unknown", subOrder));
                    }
                }
            }

            categories.Add(new KufarCategory(
                catId,
                catDto.Name ?? "Unknown",
                catOrder,
                subcategories.OrderBy(s => s.Order).ToList()
            ));
        }

        return categories.OrderBy(c => c.Order).ToList();
    }

    public async Task<IReadOnlyList<FilterSpec>> GetFiltersForSubcategoryAsync(int subcategoryId, int parentCategoryId, CancellationToken ct = default)
    {
        string url =
            $"https://api.kufar.by/taxonomy-proxy/v1/dispatch?routing=web_generalist&application=ad_listing&platform=web&taxonomy-version=1&parent={parentCategoryId}";

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var dispatch = await JsonSerializer.DeserializeAsync<KufarParametersResponseDto>(stream, JsonOptions, ct);

        return FilterSpecFactory.FromDispatchForSubcategory(dispatch, subcategoryId);
    }
}
