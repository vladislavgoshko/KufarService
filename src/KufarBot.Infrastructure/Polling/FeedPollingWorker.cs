using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    public int PollingIntervalSeconds { get; set; } = 45;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaxPagesPerPoll { get; set; } = 3;
    public int PageSize { get; set; } = 50;
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

    public async Task PollSingleFeedAsync(AppDbContext dbContext, IKufarApiClient kufarClient, PollingFeedEntity feed, CancellationToken ct)
    {
        string? cursor = null;
        var collectedAds = new List<Advertisement>();
        int pagesFetched = 0;
        string? nextCursor = null;

        while (pagesFetched < _options.MaxPagesPerPoll)
        {
            var criteria = new KufarSearchCriteria
            {
                CategoryId = feed.SubcategoryId,
                AreaId = feed.CityId,
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

        var activeSearches = await dbContext.UserSearches
            .Where(s => !s.IsPaused &&
                        s.SubcategoryId == feed.SubcategoryId &&
                        (feed.CityId == null || s.CityId == feed.CityId))
            .ToListAsync(ct);

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

                    if (notifiedUserIdsThisAd.Contains(search.UserId))
                    {
                        continue;
                    }

                    bool alreadyDelivered = await dbContext.DeliveredAds
                        .AnyAsync(d => d.UserId == search.UserId && d.AdId == ad.Id, ct);

                    if (alreadyDelivered)
                    {
                        notifiedUserIdsThisAd.Add(search.UserId);
                        continue;
                    }

                    notifiedUserIdsThisAd.Add(search.UserId);

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
                        Title = ad.Title,
                        PriceFormatted = ad.FormattedPrice,
                        Location = ad.Location,
                        AdLink = ad.AdLink,
                        ImageUrl = ad.ImageUrl,
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
    public long AdId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PriceFormatted { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string AdLink { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
