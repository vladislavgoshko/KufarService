using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Infrastructure.Persistence;
using KufarBot.Infrastructure.TelegramClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace KufarBot.Infrastructure.Polling;

public class OutboxDeliveryOptions
{
    public const string SectionName = "Outbox";

    public int PollingIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
    public int LeaseDurationSeconds { get; set; } = 60;
    public double MessagesPerSecondPerUser { get; set; } = 1.0;
    public double MessagesPerSecondGlobal { get; set; } = 25.0;
    public int RateLimitMaxEntries { get; set; } = 1000;
    public int RateLimitEntryStaleHours { get; set; } = 1;
}

public class OutboxDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxDeliveryWorker> _logger;
    private readonly OutboxDeliveryOptions _options;

    private DateTime _lastGlobalSendTime = DateTime.MinValue;
    private readonly Dictionary<long, DateTime> _lastUserSendTimes = new();
    private readonly object _rateLimitLock = new();

    public OutboxDeliveryWorker(
        IServiceProvider serviceProvider,
        ILogger<OutboxDeliveryWorker> logger,
        OutboxDeliveryOptions? options = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new OutboxDeliveryOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[OutboxDeliveryWorker] Started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            int processedCount = 0;
            try
            {
                processedCount = await ProcessOutboxBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OutboxDeliveryWorker] Unhandled error during batch processing.");
            }

            var delay = processedCount == 0 
                ? TimeSpan.FromSeconds(_options.PollingIntervalSeconds) 
                : TimeSpan.FromMilliseconds(100);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[OutboxDeliveryWorker] Stopped.");
    }

    public async Task<int> ProcessOutboxBatchAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var telegramSender = scope.ServiceProvider.GetRequiredService<ITelegramSender>();

        await RecoverExpiredLeasesAsync(dbContext, ct);

        var now = DateTime.UtcNow;

        var pendingItems = await dbContext.NotificationOutbox
            .Where(o => (o.Status == 0 || o.Status == 3) && o.NextAttemptAtUtc <= now)
            .OrderBy(o => o.NextAttemptAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (pendingItems.Count == 0)
        {
            return 0;
        }

        var lockedUntil = now.AddSeconds(_options.LeaseDurationSeconds);
        foreach (var item in pendingItems)
        {
            item.Status = 1; // Sending
            item.LockedUntilUtc = lockedUntil;
        }
        await dbContext.SaveChangesAsync(ct);

        int sentCount = 0;

        foreach (var item in pendingItems)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            await ProcessSingleItemAsync(dbContext, telegramSender, item, ct);
            sentCount++;
        }

        return sentCount;
    }

    private async Task RecoverExpiredLeasesAsync(AppDbContext dbContext, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expiredItems = await dbContext.NotificationOutbox
            .Where(o => o.Status == 1 && o.LockedUntilUtc != null && o.LockedUntilUtc < now)
            .Take(50)
            .ToListAsync(ct);

        if (expiredItems.Count > 0)
        {
            _logger.LogWarning("[OutboxDeliveryWorker] Recovering {Count} expired outbox leases.", expiredItems.Count);
            foreach (var item in expiredItems)
            {
                item.Status = 3; // Failed
                item.LockedUntilUtc = null;
                item.NextAttemptAtUtc = now;
            }
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessSingleItemAsync(
        AppDbContext dbContext, 
        ITelegramSender telegramSender, 
        NotificationOutboxEntity item, 
        CancellationToken ct)
    {
        OutboxPayload? payload = null;
        string messageText = string.Empty;
        try
        {
            payload = JsonSerializer.Deserialize<OutboxPayload>(item.PayloadJson);
            if (payload == null) throw new InvalidOperationException("Payload deserialized to null.");

            messageText = FormatHtmlMessage(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OutboxDeliveryWorker] Failed to format payload for OutboxId={OutboxId}", item.Id);
            await MarkAsDeadLetterAsync(dbContext, item, "Payload formatting error: " + ex.Message, ct);
            return;
        }

        item.AttemptCount++;

        try
        {
            await ApplyRateLimitsAsync(item.UserId, ct);

            await SendAdMessageAsync(telegramSender, item.UserId, payload, messageText, BuildReplyMarkup(payload), ct);

            item.Status = 2; // Sent
            item.SentAtUtc = DateTime.UtcNow;
            item.LockedUntilUtc = null;
            item.LastError = null;

            _logger.LogDebug("[OutboxDeliveryWorker] Successfully sent OutboxId={OutboxId} to UserId={UserId}", item.Id, item.UserId);
        }
        catch (ApiRequestException apiEx)
        {
            _logger.LogWarning(apiEx, "[OutboxDeliveryWorker] Telegram API Exception for OutboxId={OutboxId}, ErrorCode={ErrorCode}", item.Id, apiEx.ErrorCode);
            
            if (apiEx.ErrorCode == 429 && apiEx.Parameters?.RetryAfter != null)
            {
                await MarkAsRetryableAsync(dbContext, item, $"Rate limited. Code 429.", TimeSpan.FromSeconds(apiEx.Parameters.RetryAfter.Value), ct);
            }
            else if (apiEx.ErrorCode == 403)
            {
                await MarkAsDeadLetterAsync(dbContext, item, $"403 Forbidden: {apiEx.Message}", ct);
                await DeactivateUserAsync(dbContext, item.UserId, ct);
            }
            else if (apiEx.ErrorCode == 400)
            {
                await MarkAsDeadLetterAsync(dbContext, item, $"400 Bad Request: {apiEx.Message}", ct);
            }
            else
            {
                await HandleTransientFailureAsync(dbContext, item, apiEx.Message, ct);
            }
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogWarning(httpEx, "[OutboxDeliveryWorker] Network error for OutboxId={OutboxId}", item.Id);
            await HandleTransientFailureAsync(dbContext, item, "Network error: " + httpEx.Message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            item.AttemptCount--;
            item.Status = 0; // Pending
            item.LockedUntilUtc = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OutboxDeliveryWorker] Unexpected error sending OutboxId={OutboxId}", item.Id);
            await HandleTransientFailureAsync(dbContext, item, "Unexpected error: " + ex.Message, ct);
        }
        
        if (!ct.IsCancellationRequested)
        {
            try
            {
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OutboxDeliveryWorker] Failed to save status update for OutboxId={OutboxId}", item.Id);
            }
        }
    }

    private async Task SendAdMessageAsync(
        ITelegramSender telegramSender,
        long userId,
        OutboxPayload payload,
        string htmlMessage,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken ct)
    {
        var photoUrl = payload.ImageUrl;

        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            await telegramSender.SendTextMessageAsync(userId, htmlMessage, replyMarkup, ct);
            return;
        }

        try
        {
            await telegramSender.SendPhotoAsync(userId, photoUrl, htmlMessage, replyMarkup, ct);
        }
        catch (ApiRequestException photoEx)
        {
            _logger.LogWarning(
                photoEx,
                "[OutboxDeliveryWorker] Photo send failed (code {Code}) for UserId={UserId}, falling back to text.",
                photoEx.ErrorCode, userId);
            await telegramSender.SendTextMessageAsync(userId, htmlMessage, replyMarkup, ct);
        }
        catch (HttpRequestException photoEx)
        {
            _logger.LogWarning(
                photoEx,
                "[OutboxDeliveryWorker] Photo network error for UserId={UserId}, falling back to text.",
                userId);
            await telegramSender.SendTextMessageAsync(userId, htmlMessage, replyMarkup, ct);
        }
    }

    /// <summary>New-ad notifications carry the ⭐ button; favorites events do not need it.</summary>
    private InlineKeyboardMarkup? BuildReplyMarkup(OutboxPayload payload)
    {
        if (payload.Kind != OutboxPayload.KindNewAd)
        {
            return null;
        }

        return new InlineKeyboardMarkup(new[]
        {
            InlineKeyboardButton.WithCallbackData("⭐ В избранное", $"fav_{payload.AdId}")
        });
    }

    private string FormatHtmlMessage(OutboxPayload payload)
    {
        var title = System.Net.WebUtility.HtmlEncode(payload.Title);
        var price = System.Net.WebUtility.HtmlEncode(payload.PriceFormatted);
        var location = System.Net.WebUtility.HtmlEncode(payload.Location);
        var link = payload.AdLink;

        switch (payload.Kind)
        {
            case OutboxPayload.KindPriceDrop:
            {
                var oldPrice = System.Net.WebUtility.HtmlEncode(payload.OldPriceFormatted ?? string.Empty);
                return $"📉 <b>Цена снизилась!</b>\n\n" +
                       $"<b>{title}</b>\n" +
                       $"💰 Цена: <b>{price}</b>" +
                       (string.IsNullOrEmpty(oldPrice) ? "" : $" (было: {oldPrice})") +
                       $"\n📍 Место: {location}\n\n" +
                       $"<a href=\"{link}\">Открыть объявление →</a>";
            }

            case OutboxPayload.KindAdMissing:
                return $"🗑 <b>Объявление больше доступно не будет.</b>\n\n" +
                       $"<b>{title}</b>\n" +
                       $"<a href=\"{link}\">Проверить объявление →</a> (удалено с Kufar)";

            default:
                return $"✨ <b>Новое объявление!</b>\n\n" +
                       $"<b>{title}</b>\n" +
                       $"💰 Цена: {price}\n" +
                       $"📍 Место: {location}\n" +
                       $"⏰ Дата: {payload.CreatedAtUtc:dd.MM.yyyy HH:mm} UTC\n\n" +
                       $"<a href=\"{link}\">Открыть объявление →</a>";
        }
    }

    private async Task HandleTransientFailureAsync(AppDbContext dbContext, NotificationOutboxEntity item, string error, CancellationToken ct)
    {
        if (item.AttemptCount >= item.MaxAttempts)
        {
            await MarkAsDeadLetterAsync(dbContext, item, $"Exhausted retries. Last error: {error}", ct);
        }
        else
        {
            int delaySeconds = (int)Math.Pow(2, item.AttemptCount);
            await MarkAsRetryableAsync(dbContext, item, error, TimeSpan.FromSeconds(delaySeconds), ct);
        }
    }

    private Task MarkAsRetryableAsync(AppDbContext dbContext, NotificationOutboxEntity item, string error, TimeSpan delay, CancellationToken ct)
    {
        item.Status = 3; // Failed
        item.LockedUntilUtc = null;
        item.LastError = error;
        item.NextAttemptAtUtc = DateTime.UtcNow.Add(delay);
        return Task.CompletedTask;
    }

    private Task MarkAsDeadLetterAsync(AppDbContext dbContext, NotificationOutboxEntity item, string error, CancellationToken ct)
    {
        item.Status = 4; // DeadLetter
        item.LockedUntilUtc = null;
        item.LastError = error;
        return Task.CompletedTask;
    }

    private async Task DeactivateUserAsync(AppDbContext dbContext, long userId, CancellationToken ct)
    {
        var user = await dbContext.Users.FindAsync(new object[] { userId }, ct);
        if (user != null && user.IsActive)
        {
            user.IsActive = false;
            _logger.LogInformation("[OutboxDeliveryWorker] Deactivated UserId={UserId} due to 403 Forbidden.", userId);
        }
    }

    private async Task ApplyRateLimitsAsync(long userId, CancellationToken ct)
    {
        TimeSpan globalDelay = TimeSpan.Zero;
        TimeSpan userDelay = TimeSpan.Zero;

        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;

            // Bound memory usage: prune stale per-user entries when the dictionary grows too large.
            if (_lastUserSendTimes.Count >= _options.RateLimitMaxEntries)
            {
                var staleCutoff = now - TimeSpan.FromHours(_options.RateLimitEntryStaleHours);
                var staleKeys = _lastUserSendTimes
                    .Where(kv => kv.Value < staleCutoff)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var key in staleKeys)
                {
                    _lastUserSendTimes.Remove(key);
                }
            }

            var globalMsPerMsg = 1000.0 / _options.MessagesPerSecondGlobal;
            var nextGlobalTime = _lastGlobalSendTime.AddMilliseconds(globalMsPerMsg);
            if (now < nextGlobalTime)
            {
                globalDelay = nextGlobalTime - now;
                _lastGlobalSendTime = nextGlobalTime;
            }
            else
            {
                _lastGlobalSendTime = now;
            }

            var userMsPerMsg = 1000.0 / _options.MessagesPerSecondPerUser;
            if (_lastUserSendTimes.TryGetValue(userId, out var lastUserTime))
            {
                var nextUserTime = lastUserTime.AddMilliseconds(userMsPerMsg);
                if (now < nextUserTime)
                {
                    userDelay = nextUserTime - now;
                    _lastUserSendTimes[userId] = nextUserTime > _lastGlobalSendTime ? nextUserTime : _lastGlobalSendTime;
                }
                else
                {
                    _lastUserSendTimes[userId] = _lastGlobalSendTime;
                }
            }
            else
            {
                _lastUserSendTimes[userId] = _lastGlobalSendTime;
            }
        }

        var delay = globalDelay > userDelay ? globalDelay : userDelay;

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, ct);
        }
    }
}
