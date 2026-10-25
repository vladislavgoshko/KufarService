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
    Task<IReadOnlyList<FilterSpec>> GetFiltersForCategoryAsync(int categoryId, CancellationToken ct = default);
}
