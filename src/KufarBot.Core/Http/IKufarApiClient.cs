using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Filters;
using KufarBot.Core.Models;

namespace KufarBot.Core.Http;

public record KufarSearchResult(
    IReadOnlyList<Advertisement> Ads,
    string? NextCursor,
    int? Total
);

public interface IKufarApiClient
{
    Task<KufarSearchResult> SearchAsync(KufarSearchCriteria criteria, CancellationToken ct = default);
    Task<IReadOnlyList<KufarCategory>> GetCategoryTreeAsync(CancellationToken ct = default);
    /// <summary>
    /// Filter parameters for one subcategory. The dispatch must be requested for the whole
    /// category (parent = category id, e.g. 16000) — subcategory-level requests (parent=16010) return 404.
    /// </summary>
    Task<IReadOnlyList<FilterSpec>> GetFiltersForSubcategoryAsync(int subcategoryId, int parentCategoryId, CancellationToken ct = default);
}
