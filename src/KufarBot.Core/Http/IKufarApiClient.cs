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
    /// Results are cached per process: the first call for the category fetches and parses the dispatch,
    /// the subsequent ones (any subcategory) are served from memory.
    /// <paramref name="conditionalValues"/> = chosen values of driver parameters (e.g. Тип),
    /// they enable the value-driven extra parameters of the chosen type.
    /// </summary>
    Task<IReadOnlyList<FilterSpec>> GetFiltersForSubcategoryAsync(
        int subcategoryId,
        int parentCategoryId,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? conditionalValues = null,
        CancellationToken ct = default);

    /// <summary>
    /// Specs for all subcategories of the category at once (one HTTP request per category, cached per process).
    /// </summary>
    Task<CategoryFilterSpecs> GetFiltersForCategoryAsync(int parentCategoryId, CancellationToken ct = default);
}
