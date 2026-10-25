using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using Microsoft.Extensions.Logging;

namespace KufarBot.App.Telegram;

public interface ICategoryService
{
    Task<IReadOnlyList<KufarCategory>> GetCategoriesAsync(CancellationToken ct = default);
    Task<KufarCategory?> GetCategoryByIdAsync(int categoryId, CancellationToken ct = default);
    Task<(KufarCategory? Category, KufarSubcategory? Subcategory)> FindSubcategoryAsync(int subcategoryId, CancellationToken ct = default);
}

public class CachedCategoryService : ICategoryService
{
    private readonly IKufarApiClient _apiClient;
    private readonly ILogger<CachedCategoryService> _logger;

    private IReadOnlyList<KufarCategory>? _cachedCategories;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _lastFetchedUtc = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public CachedCategoryService(IKufarApiClient apiClient, ILogger<CachedCategoryService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<KufarCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        if (_cachedCategories != null && DateTime.UtcNow - _lastFetchedUtc < CacheTtl)
        {
            return _cachedCategories;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedCategories != null && DateTime.UtcNow - _lastFetchedUtc < CacheTtl)
            {
                return _cachedCategories;
            }

            _logger.LogInformation("[CachedCategoryService] Fetching category tree from Kufar API...");
            var categories = await _apiClient.GetCategoryTreeAsync(ct);
            _cachedCategories = categories;
            _lastFetchedUtc = DateTime.UtcNow;
            return categories;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<KufarCategory?> GetCategoryByIdAsync(int categoryId, CancellationToken ct = default)
    {
        var categories = await GetCategoriesAsync(ct);
        return categories.FirstOrDefault(c => c.Id == categoryId);
    }

    public async Task<(KufarCategory? Category, KufarSubcategory? Subcategory)> FindSubcategoryAsync(int subcategoryId, CancellationToken ct = default)
    {
        var categories = await GetCategoriesAsync(ct);
        foreach (var cat in categories)
        {
            var sub = cat.Subcategories.FirstOrDefault(s => s.Id == subcategoryId);
            if (sub != null)
            {
                return (cat, sub);
            }
        }

        return (null, null);
    }
}
