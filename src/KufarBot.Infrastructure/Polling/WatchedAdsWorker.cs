using System;
using System.Collections.Generic;
using System.Linq;
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

public class WatchedAdsOptions
{
    public const string SectionName = "WatchedAds";

    public int IntervalSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 10;
    /// <summary>Minimum delay between checking the same ad twice.</summary>
    public int RecheckIntervalMinutes { get; set; } = 60;
    /// <summary>After an API error the ad becomes due again after this delay instead of the full interval.</summary>
    public int RetryDelayMinutes { get; set; } = 10;
}

/// <summary>
/// eph-8b: re-checks the user's favorite ads (⭐) every RecheckIntervalMinutes:
/// a price drop → outbox notification, disappearance (404) → one-time notification.
/// </summary>
public class WatchedAdsWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WatchedAdsWorker> _logger;
    private readonly WatchedAdsOptions _options;

    public WatchedAdsWorker(
        IServiceProvider serviceProvider,
        ILogger<WatchedAdsWorker> logger,
        WatchedAdsOptions? options = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new WatchedAdsOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WatchedAdsWorker] Started.");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WatchedAdsWorker] Unhandled error in watched-ads iteration.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[WatchedAdsWorker] Stopped.");
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        int processed = 0;
        var recheckThreshold = DateTime.UtcNow.AddMinutes(-_options.RecheckIntervalMinutes);

        while (processed < _options.BatchSize && !ct.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var kufarClient = scope.ServiceProvider.GetRequiredService<IKufarApiClient>();

            var batch = await dbContext.WatchedAds
                .Where(w => w.LastCheckedUtc <= recheckThreshold)
                .OrderBy(w => w.LastCheckedUtc)
                .Take(_options.BatchSize - processed)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var watched in batch)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                await ProcessSingleAsync(dbContext, kufarClient, watched, ct);
                processed++;
            }
        }

        return processed;
    }

    private async Task ProcessSingleAsync(AppDbContext dbContext, IKufarApiClient kufarClient, WatchedAdEntity watched, CancellationToken ct)
    {
        try
        {
            Advertisement? details = await kufarClient.GetAdDetailsAsync(watched.AdId, ct);

            if (details == null)
            {
                watched.LastCheckedUtc = DateTime.UtcNow;

                if (!watched.NotifiedMissing)
                {
                    await EnqueueAsync(dbContext, watched, new OutboxPayload
                    {
                        AdId = watched.AdId,
                        Kind = OutboxPayload.KindAdMissing,
                        Title = watched.Title,
                        AdLink = watched.AdLink,
                        ImageUrl = watched.ImageUrl,
                        CreatedAtUtc = watched.LastCheckedUtc
                    }, ct);
                    watched.NotifiedMissing = true;
                }
            }
            else
            {
                watched.LastCheckedUtc = DateTime.UtcNow;
                watched.NotifiedMissing = false;

                if (details.PriceByn.HasValue && watched.LastPriceByn.HasValue &&
                    details.PriceByn.Value < watched.LastPriceByn.Value - 0.005m)
                {
                    await EnqueueAsync(dbContext, watched, new OutboxPayload
                    {
                        AdId = watched.AdId,
                        Kind = OutboxPayload.KindPriceDrop,
                        Title = details.Title,
                        PriceFormatted = details.FormattedPrice,
                        OldPriceFormatted = watched.LastPriceByn.HasValue
                            ? FormattedPrice(watched.LastPriceByn.Value)
                            : null,
                        Location = details.Location,
                        AdLink = details.AdLink,
                        ImageUrl = details.ImageUrl,
                        AllImageUrls = new List<string>(details.AllImageUrls),
                        CreatedAtUtc = DateTime.UtcNow
                    }, ct);
                }

                // fresh snapshot of the details
                watched.Title = details.Title;
                watched.AdLink = details.AdLink;
                watched.ImageUrl = details.ImageUrl;
                watched.Location = details.Location;
                watched.LastPriceByn = details.PriceByn ?? watched.LastPriceByn;
            }

            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WatchedAdsWorker] Failed to check watched ad AdId={AdId}", watched.AdId);

            // Retry delay instead of the full recheck interval; still out of the "due"
            // window so the current batch loop does not re-select the same row.
            watched.LastCheckedUtc = DateTime.UtcNow.AddMinutes(
                _options.RetryDelayMinutes - _options.RecheckIntervalMinutes);
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private static async Task EnqueueAsync(AppDbContext dbContext, WatchedAdEntity watched, OutboxPayload payload, CancellationToken ct)
    {
        dbContext.NotificationOutbox.Add(new NotificationOutboxEntity
        {
            UserId = watched.UserId,
            SearchId = 0,
            AdId = watched.AdId,
            Status = 0,
            PayloadJson = JsonSerializer.Serialize(payload),
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await Task.CompletedTask;
    }

    private static string FormattedPrice(decimal price) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{price:0.##} BYN");
}
