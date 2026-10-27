using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Filters;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KufarBot.Infrastructure.Polling;

public class FeedPollingOptions
{
    public const string SectionName = "Polling";

    public int PollingIntervalSeconds { get; set; } = 60;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaxPagesPerPoll { get; set; } = 3;
    public int PageSize { get; set; } = 50;

    /// <summary>eph-13: base delay for the anti-ban exponential backoff after consecutive errors.</summary>
    public int BackoffBaseSeconds { get; set; } = 90;
    /// <summary>eph-13: upper bound of the backoff delay.</summary>
    public int BackoffMaxSeconds { get; set; } = 1800;
}

public class FeedPollingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeedPollingWorker> _logger;
    private readonly FeedPollingOptions _options;

    public FeedPollingWorker(
        IServiceProvider serviceProvider,
        ILogger<FeedPollingWorker> logger,
        FeedPollingOptions? options = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new FeedPollingOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[FeedPollingWorker] Started.");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(_options.PollingIntervalSeconds);
            try
            {
                await PollAllFeedsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FeedPollingWorker] Unhandled error in polling iteration.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[FeedPollingWorker] Stopped.");
    }

    public async Task PollAllFeedsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var kufarClient = scope.ServiceProvider.GetRequiredService<IKufarApiClient>();

        List<PollingFeedEntity> feeds;
        try
        {
            feeds = await dbContext.PollingFeeds.ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FeedPollingWorker] Failed to load polling feeds from database.");
            return;
        }

        if (feeds.Count == 0)
        {
            return;
        }

        foreach (var feed in feeds)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            // eph-13: anti-ban exponential backoff — skip a feed that recently
            // failed until its backoff window (base * 2^(n-1) with jitter) has elapsed.
            if (feed.ConsecutiveErrors > 0 && feed.LastErrorAtUtc.HasValue)
            {
                TimeSpan delay = ComputeBackoffDelay(
                    feed.ConsecutiveErrors, _options.BackoffBaseSeconds, _options.BackoffMaxSeconds, BackoffJitter.Value);
                if (DateTime.UtcNow < feed.LastErrorAtUtc.Value + delay)
                {
                    continue;
                }
            }

            try
            {
                await PollSingleFeedAsync(dbContext, kufarClient, feed, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FeedPollingWorker] Error polling feed Id={FeedId}, Subcategory={Subcategory}, City={City}",
                    feed.Id, feed.SubcategoryId, feed.CityId);

                feed.ConsecutiveErrors++;
                feed.LastErrorAtUtc = DateTime.UtcNow;
                try
                {
                    await dbContext.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "[FeedPollingWorker] Failed to update error count for feed Id={FeedId}", feed.Id);
                }
            }
        }
    }

    private static readonly ThreadLocal<Random> BackoffJitter = new(() => new Random());

    /// <summary>
    /// eph-13: backoff window for n consecutive failures — base * 2^(n-1), capped at maxSeconds,
    /// multiplied by a ±20% jitter (so parallel feeds do not retry in lockstep).
    /// </summary>
    public static TimeSpan ComputeBackoffDelay(int consecutiveErrors, int baseSeconds, int maxSeconds, Random? jitter = null)
    {
        int n = Math.Max(1, consecutiveErrors);
        double exponent = Math.Min(n - 1, 30); // guard against overflow for absurd counters
        double raw = Math.Min(baseSeconds * Math.Pow(2, exponent), maxSeconds);
        double factor = jitter == null ? 1.0 : 0.8 + jitter.NextDouble() * 0.4;
        return TimeSpan.FromSeconds(raw * factor);
    }

    public async Task PollSingleFeedAsync(AppDbContext dbContext, IKufarApiClient kufarClient, PollingFeedEntity feed, CancellationToken ct)
    {
        string? cursor = null;
        var collectedAds = new List<Advertisement>();
        int pagesFetched = 0;
        string? nextCursor = null;

        var feedUrlFilters = UrlFiltersBuilder.Parse(feed.UrlFiltersJson);
        bool usedUrlFilters = feedUrlFilters.Count > 0;

        while (pagesFetched < _options.MaxPagesPerPoll)
        {
            var criteria = new KufarSearchCriteria
            {
                CategoryId = feed.SubcategoryId,
                AreaId = feed.CityId,
                UrlFilters = usedUrlFilters ? feedUrlFilters : null,
                Size = _options.PageSize,
                Cursor = cursor,
                Sort = "lst.d",
                Currency = "BYN"
            };

            KufarSearchResult result;
            try
            {
                result = await kufarClient.SearchAsync(criteria, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FeedPollingWorker] HTTP search failed for feed Id={FeedId}, Subcategory={Subcategory}", feed.Id, feed.SubcategoryId);

                // Unknown/invalid URL filter names make Kufar reject the whole request (422).
                // Fall back to the bare feed once per poll — saved filters are still applied
                // client-side (search.Matches), so correctness is preserved.
                if (usedUrlFilters && cursor == null)
                {
                    usedUrlFilters = false;
                    _logger.LogInformation("[FeedPollingWorker] Retrying feed Id={FeedId} without URL filters (client-side matching).", feed.Id);
                    continue;
                }

                throw;
            }

            pagesFetched++;
            if (result.Ads.Count > 0)
            {
                collectedAds.AddRange(result.Ads);
            }

            nextCursor = result.NextCursor;

            if (result.Ads.Count == 0 || string.IsNullOrWhiteSpace(nextCursor))
            {
                break;
            }

            var oldestInBatch = result.Ads[^1];
            if (oldestInBatch.ListTimeUtc < feed.LastSeenAdTimeUtc)
            {
                break;
            }

            cursor = nextCursor;
        }

        if (collectedAds.Count == 0)
        {
            feed.LastPolledAtUtc = DateTime.UtcNow;
            feed.ConsecutiveErrors = 0;
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        var lastSeenIds = SearchMatchingExtensions.DeserializeIds(feed.LastSeenAdIdsJson);

        var newAds = collectedAds
            .Where(ad => ad.ListTimeUtc > feed.LastSeenAdTimeUtc ||
                        (ad.ListTimeUtc == feed.LastSeenAdTimeUtc && !lastSeenIds.Contains(ad.Id)))
            .OrderBy(ad => ad.ListTimeUtc)
            .ToList();

        if (newAds.Count == 0)
        {
            feed.LastPolledAtUtc = DateTime.UtcNow;
            feed.ConsecutiveErrors = 0;
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        var maxTime = newAds.Max(ad => ad.ListTimeUtc);
        var maxIds = newAds
            .Where(ad => ad.ListTimeUtc == maxTime)
            .Select(ad => ad.Id)
            .Distinct()
            .ToList();

        // A search must be served by exactly *its* corresponding feed:
        //   feed (subcat, city=X, filters=F)   -> searches (subcat, city=X, URL filters == F)
        //   feed (subcat, null, filters=F)     -> searches with CityId == null ("All Belarus")
        // Attaching city searches to the "all Belarus" feed would notify users about
        // ads from other cities (the client-side price matching cannot tell them apart).
        var searchesForCity = await dbContext.UserSearches
            .Where(s => !s.IsPaused &&
                        s.SubcategoryId == feed.SubcategoryId &&
                        ((feed.CityId == null && s.CityId == null) ||
                         (feed.CityId != null && s.CityId == feed.CityId)))
            .ToListAsync(ct);

        string feedUrlFiltersJson = UrlFiltersBuilder.CanonicalJson(feedUrlFilters);
        var activeSearches = searchesForCity
            .Where(s => string.Equals(
                UrlFiltersBuilder.CanonicalJson(UrlFiltersBuilder.Build(SavedFilter.Deserialize(s.FiltersJson))),
                feedUrlFiltersJson,
                StringComparison.Ordinal))
            .ToList();

        // Bulk-load already-delivered (UserId, AdId) pairs to avoid per-pair AnyAsync queries (N+1).
        var newAdIds = newAds.Select(a => a.Id).ToList();
        var candidateUserIds = activeSearches
            .Select(s => s.UserId)
            .Distinct()
            .ToList();
        var deliveredKeySet = new HashSet<(long UserId, long AdId)>();
        if (newAdIds.Count > 0 && candidateUserIds.Count > 0)
        {
            var deliveredPairs = await dbContext.DeliveredAds
                .AsNoTracking()
                .Where(d => newAdIds.Contains(d.AdId) && candidateUserIds.Contains(d.UserId))
                .Select(d => new { d.UserId, d.AdId })
                .ToListAsync(ct);
            deliveredKeySet = deliveredPairs
                .Select(p => (p.UserId, p.AdId))
                .ToHashSet();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var ad in newAds)
            {
                // Track users already notified for this ad in the current batch to prevent duplicate outbox entries
                var notifiedUserIdsThisAd = new HashSet<long>();

                foreach (var search in activeSearches)
                {
                    if (!search.Matches(ad))
                    {
                        continue;
                    }

                    if (!notifiedUserIdsThisAd.Add(search.UserId))
                    {
                        continue;
                    }

                    if (deliveredKeySet.Contains((search.UserId, ad.Id)))
                    {
                        continue;
                    }

                    dbContext.DeliveredAds.Add(new DeliveredAdEntity
                    {
                        UserId = search.UserId,
                        AdId = ad.Id,
                        MatchedSearchId = search.Id,
                        DeliveredAtUtc = DateTime.UtcNow
                    });

                    var payload = new OutboxPayload
                    {
                        AdId = ad.Id,
                        Kind = OutboxPayload.KindNewAd,
                        Title = ad.Title,
                        PriceFormatted = ad.FormattedPrice,
                        Location = ad.Location,
                        AdLink = ad.AdLink,
                        ImageUrl = ad.ImageUrl,
                        AllImageUrls = new List<string>(ad.AllImageUrls),
                        CreatedAtUtc = ad.ListTimeUtc
                    };

                    string payloadJson = JsonSerializer.Serialize(payload);

                    dbContext.NotificationOutbox.Add(new NotificationOutboxEntity
                    {
                        UserId = search.UserId,
                        SearchId = search.Id,
                        AdId = ad.Id,
                        Status = 0,
                        PayloadJson = payloadJson,
                        AttemptCount = 0,
                        MaxAttempts = 5,
                        NextAttemptAtUtc = DateTime.UtcNow,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            feed.LastSeenAdTimeUtc = maxTime;
            feed.LastSeenAdIdsJson = SearchMatchingExtensions.SerializeIds(maxIds);
            feed.LastPolledAtUtc = DateTime.UtcNow;
            feed.ConsecutiveErrors = 0;

            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("[FeedPollingWorker] Feed Id={FeedId} processed {Count} new ad(s). Checkpoint advanced to {MaxTime}.",
                feed.Id, newAds.Count, maxTime);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}

public class OutboxPayload
{
    public const string KindNewAd = "new_ad";
    public const string KindPriceDrop = "fav_price_drop";
    public const string KindAdMissing = "fav_missing";

    public long AdId { get; set; }
    /// <summary>Message template: new_ad (notification with the ⭐ button), fav_price_drop, fav_missing.</summary>
    public string Kind { get; set; } = KindNewAd;
    public string Title { get; set; } = string.Empty;
    public string PriceFormatted { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string AdLink { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    /// <summary>All ad images (eph-9): >1 → the message is sent as a photo album.</summary>
    public List<string> AllImageUrls { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Price shown in price-drop notifications ("was X").</summary>
    public string? OldPriceFormatted { get; set; }
}
