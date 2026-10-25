using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KufarBot.App.Telegram;

public interface ISearchSubscriptionService
{
    Task<UserSearchEntity> SaveSearchAsync(
        long userId,
        int subcategoryId,
        string subcategoryTitle,
        int? cityId,
        string? cityName,
        decimal? minPriceByn,
        decimal? maxPriceByn,
        string? filtersJson = null,
        CancellationToken ct = default);

    Task<bool> UpdateSearchFiltersAsync(int searchId, long userId, string filtersJson, CancellationToken ct = default);
    Task<IReadOnlyList<UserSearchEntity>> GetUserSearchesAsync(long userId, CancellationToken ct = default);
    Task<UserSearchEntity?> GetSearchByIdAsync(int searchId, long userId, CancellationToken ct = default);
    Task<bool> TogglePauseAsync(int searchId, long userId, bool isPaused, CancellationToken ct = default);
    Task<bool> DeleteSearchAsync(int searchId, long userId, CancellationToken ct = default);
}

public class SearchSubscriptionService : ISearchSubscriptionService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<SearchSubscriptionService> _logger;

    public SearchSubscriptionService(AppDbContext dbContext, ILogger<SearchSubscriptionService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UserSearchEntity> SaveSearchAsync(
        long userId,
        int subcategoryId,
        string subcategoryTitle,
        int? cityId,
        string? cityName,
        decimal? minPriceByn,
        decimal? maxPriceByn,
        string? filtersJson = null,
        CancellationToken ct = default)
    {
        if (minPriceByn.HasValue && maxPriceByn.HasValue && minPriceByn.Value > maxPriceByn.Value)
        {
            throw new ArgumentException("Минимальная цена не может быть больше максимальной.");
        }

        filtersJson ??= "[]";

        // 1. Check if identical active/paused search already exists for user to ensure idempotency
        var existing = await _dbContext.UserSearches
            .FirstOrDefaultAsync(s => s.UserId == userId &&
                                      s.SubcategoryId == subcategoryId &&
                                      s.CityId == cityId &&
                                      s.MinPriceByn == minPriceByn &&
                                      s.MaxPriceByn == maxPriceByn, ct);

        if (existing != null)
        {
            bool modified = false;
            if (existing.IsPaused)
            {
                existing.IsPaused = false;
                modified = true;
            }

            if (!string.Equals(existing.FiltersJson ?? "[]", filtersJson, StringComparison.Ordinal))
            {
                existing.FiltersJson = filtersJson;
                modified = true;
            }

            if (modified)
            {
                existing.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(ct);
            }

            await EnsurePollingFeedExistsAsync(subcategoryId, cityId, ct);
            return existing;
        }

        // 2. Create search
        var now = DateTime.UtcNow;
        var search = new UserSearchEntity
        {
            UserId = userId,
            SubcategoryId = subcategoryId,
            SubcategoryTitle = subcategoryTitle,
            CityId = cityId,
            CityName = cityName,
            MinPriceByn = minPriceByn,
            MaxPriceByn = maxPriceByn,
            FiltersJson = filtersJson,
            IsPaused = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.UserSearches.Add(search);

        // 3. Ensure corresponding PollingFeed exists
        await EnsurePollingFeedExistsAsync(subcategoryId, cityId, ct);

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("[SearchSubscriptionService] Created search Id={SearchId} for User={UserId}", search.Id, userId);

        return search;
    }

    public async Task<bool> UpdateSearchFiltersAsync(int searchId, long userId, string filtersJson, CancellationToken ct = default)
    {
        var search = await GetSearchByIdAsync(searchId, userId, ct);
        if (search == null)
        {
            return false;
        }

        search.FiltersJson = string.IsNullOrWhiteSpace(filtersJson) ? "[]" : filtersJson;
        search.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("[SearchSubscriptionService] Updated filters for search Id={SearchId}, User={UserId}", searchId, userId);
        return true;
    }

    private async Task EnsurePollingFeedExistsAsync(int subcategoryId, int? cityId, CancellationToken ct)
    {
        bool feedExists = await _dbContext.PollingFeeds
            .AnyAsync(f => f.SubcategoryId == subcategoryId && f.CityId == cityId, ct);

        if (!feedExists)
        {
            _logger.LogInformation("[SearchSubscriptionService] Creating PollingFeed for Subcategory={Subcategory}, City={City}", subcategoryId, cityId);
            _dbContext.PollingFeeds.Add(new PollingFeedEntity
            {
                SubcategoryId = subcategoryId,
                CityId = cityId,
                LastSeenAdTimeUtc = DateTime.UtcNow,
                LastSeenAdIdsJson = "[]",
                ConsecutiveErrors = 0
            });
        }
    }

    public async Task<IReadOnlyList<UserSearchEntity>> GetUserSearchesAsync(long userId, CancellationToken ct = default)
    {
        return await _dbContext.UserSearches
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<UserSearchEntity?> GetSearchByIdAsync(int searchId, long userId, CancellationToken ct = default)
    {
        return await _dbContext.UserSearches
            .FirstOrDefaultAsync(s => s.Id == searchId && s.UserId == userId, ct);
    }

    public async Task<bool> TogglePauseAsync(int searchId, long userId, bool isPaused, CancellationToken ct = default)
    {
        var search = await _dbContext.UserSearches
            .FirstOrDefaultAsync(s => s.Id == searchId && s.UserId == userId, ct);

        if (search == null)
        {
            return false;
        }

        search.IsPaused = isPaused;
        search.UpdatedAtUtc = DateTime.UtcNow;

        // Note: PollingFeed remains alive because feeds can be shared by multiple users/searches,
        // and FeedPollingWorker queries dbContext.UserSearches where !IsPaused during ad delivery.
        // If no active searches exist across the whole DB for that feed, clean up can be evaluated.
        await CleanupOrphanedFeedsIfAnyAsync(search.SubcategoryId, search.CityId, ct);

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("[SearchSubscriptionService] Search Id={SearchId} pause status set to {IsPaused}", searchId, isPaused);
        return true;
    }

    public async Task<bool> DeleteSearchAsync(int searchId, long userId, CancellationToken ct = default)
    {
        var search = await _dbContext.UserSearches
            .FirstOrDefaultAsync(s => s.Id == searchId && s.UserId == userId, ct);

        if (search == null)
        {
            return false;
        }

        int subcategoryId = search.SubcategoryId;
        int? cityId = search.CityId;

        // Pending outbox notifications for this search:
        // Cancel pending notifications that haven't been sent yet so deleted search doesn't spam user
        var pendingOutbox = await _dbContext.NotificationOutbox
            .Where(o => o.SearchId == searchId && o.Status == 0)
            .ToListAsync(ct);

        foreach (var item in pendingOutbox)
        {
            item.Status = 4; // DeadLetter / Cancelled
            item.LastError = "Search deleted by user";
        }

        _dbContext.UserSearches.Remove(search);
        await _dbContext.SaveChangesAsync(ct);

        // Check if any other ACTIVE OR PAUSED searches still reference this PollingFeed
        await CleanupOrphanedFeedsIfAnyAsync(subcategoryId, cityId, ct);

        _logger.LogInformation("[SearchSubscriptionService] Deleted search Id={SearchId} for User={UserId}", searchId, userId);
        return true;
    }

    private async Task CleanupOrphanedFeedsIfAnyAsync(int subcategoryId, int? cityId, CancellationToken ct)
    {
        bool otherSearchesExist = await _dbContext.UserSearches
            .AnyAsync(s => s.SubcategoryId == subcategoryId && s.CityId == cityId, ct);

        if (!otherSearchesExist)
        {
            var orphanedFeed = await _dbContext.PollingFeeds
                .FirstOrDefaultAsync(f => f.SubcategoryId == subcategoryId && f.CityId == cityId, ct);

            if (orphanedFeed != null)
            {
                _logger.LogInformation("[SearchSubscriptionService] Removing orphaned PollingFeed Id={FeedId} (Subcategory={Subcategory}, City={City})",
                    orphanedFeed.Id, subcategoryId, cityId);
                _dbContext.PollingFeeds.Remove(orphanedFeed);
                await _dbContext.SaveChangesAsync(ct);
            }
        }
    }
}
