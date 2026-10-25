using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreAPI.Models;
using Microsoft.Extensions.Logging;

namespace CoreAPI.Services
{
    public class AdTrackingService
    {
        private readonly AdSearchService _adSearchService;
        private readonly ILogger<AdTrackingService> _logger;
        private readonly ConcurrentDictionary<string, ActiveTracking> _activeTrackings;

        private class ActiveTracking
        {
            public int SubcategoryId { get; set; }
            public int CategoryId { get; set; }
            public string Filter { get; set; }
            public DateTime LastSeenAdTime { get; set; }
            public HashSet<long> LastSeenAdIds { get; set; } = new();
            public ConcurrentQueue<Advertisement> UnreadAds { get; set; } = new();
            public CancellationTokenSource CancellationTokenSource { get; set; }
            public Task TrackingTask { get; set; }
            public DateTime StartedAt { get; set; }
            public string ClientId { get; set; }
            public Func<List<Advertisement>, Task> OnNewAdvertisementsCallback { get; set; }
        }

        public AdTrackingService(AdSearchService adSearchService, ILogger<AdTrackingService> logger)
        {
            _adSearchService = adSearchService;
            _logger = logger;
            _activeTrackings = new ConcurrentDictionary<string, ActiveTracking>();
        }

        public async Task StartTrackingAsync(
            string trackingId,
            int categoryId,
            int subcategoryId,
            string filter,
            Func<List<Advertisement>, Task> onNewAdvertisementsCallback = null)
        {
            if (_activeTrackings.ContainsKey(trackingId))
            {
                await StopTrackingAsync(trackingId);
            }

            var cts = new CancellationTokenSource();
            var tracking = new ActiveTracking
            {
                SubcategoryId = subcategoryId,
                CategoryId = categoryId,
                Filter = filter,
                CancellationTokenSource = cts,
                StartedAt = DateTime.Now,
                ClientId = trackingId,
                OnNewAdvertisementsCallback = onNewAdvertisementsCallback,
                LastSeenAdTime = DateTime.MinValue
            };

            // Запрашиваем стартовые объявления с учетом фильтра города (filter)
            var initialAds = await _adSearchService.SearchByCategoryAsync(subcategoryId, filter, 5);
            if (initialAds.Any())
            {
                var maxTime = initialAds.Max(ad => ad.CreatedAt);
                tracking.LastSeenAdTime = maxTime;
                tracking.LastSeenAdIds = initialAds
                    .Where(ad => ad.CreatedAt == maxTime)
                    .Select(ad => ad.Id)
                    .ToHashSet();
            }
            else
            {
                tracking.LastSeenAdTime = DateTime.UtcNow;
            }

            tracking.TrackingTask = TrackingLoopAsync(tracking, cts.Token);
            _activeTrackings.TryAdd(trackingId, tracking);

            _logger.LogInformation(
                $"[AdTrackingService] Отслеживание начато. TrackingId: {trackingId}, " +
                $"CategoryId: {categoryId}, Subcategory: {subcategoryId}, Фильтр: {filter}");
        }

        public async Task StopTrackingAsync(string trackingId)
        {
            if (_activeTrackings.TryRemove(trackingId, out var tracking))
            {
                tracking.CancellationTokenSource.Cancel();
                try
                {
                    await tracking.TrackingTask;
                }
                catch (OperationCanceledException) { }

                _logger.LogInformation($"[AdTrackingService] Отслеживание остановлено. TrackingId: {trackingId}");
            }
        }

        public Dictionary<string, TrackingStatus> GetActiveTrackings()
        {
            return _activeTrackings.ToDictionary(
                kvp => kvp.Key,
                kvp => new TrackingStatus
                {
                    TrackingId = kvp.Key,
                    CategoryId = kvp.Value.CategoryId,
                    SubcategoryId = kvp.Value.SubcategoryId,
                    Filter = kvp.Value.Filter,
                    StartedAt = kvp.Value.StartedAt,
                    PendingCount = kvp.Value.UnreadAds.Count
                }
            );
        }

        public async Task<List<Advertisement>> GetNewAdvertisementsAsync(string trackingId)
        {
            if (!_activeTrackings.TryGetValue(trackingId, out var tracking))
            {
                _logger.LogWarning($"[AdTrackingService] Отслеживание не найдено. TrackingId: {trackingId}");
                return new List<Advertisement>();
            }

            var newAds = new List<Advertisement>();
            while (tracking.UnreadAds.TryDequeue(out var ad))
            {
                newAds.Add(ad);
            }

            return await Task.FromResult(newAds);
        }

        private async Task TrackingLoopAsync(ActiveTracking tracking, CancellationToken ct)
        {
            const int intervalSeconds = 45;
            var delayMs = intervalSeconds * 1000;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Важно: передаем во внешний поиск по Kufar ID подкатегории и фильтр по городу
                        var allAds = await _adSearchService.SearchByCategoryAsync(tracking.SubcategoryId, tracking.Filter, 2);

                        var newAds = allAds
                            .Where(ad => ad.CreatedAt > tracking.LastSeenAdTime ||
                                        (ad.CreatedAt == tracking.LastSeenAdTime && !tracking.LastSeenAdIds.Contains(ad.Id)))
                            .OrderBy(ad => ad.CreatedAt)
                            .ToList();

                        if (newAds.Count > 0)
                        {
                            var maxTime = newAds.Max(ad => ad.CreatedAt);
                            tracking.LastSeenAdTime = maxTime;

                            tracking.LastSeenAdIds = newAds
                                .Where(ad => ad.CreatedAt == maxTime)
                                .Select(ad => ad.Id)
                                .ToHashSet();

                            foreach (var ad in newAds)
                            {
                                tracking.UnreadAds.Enqueue(ad);
                            }

                            if (tracking.OnNewAdvertisementsCallback != null)
                            {
                                try
                                {
                                    await tracking.OnNewAdvertisementsCallback(newAds);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"[AdTrackingService] Ошибка при вызове callback: {ex.Message}");
                                }
                            }
                        }

                        await Task.Delay(delayMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[AdTrackingService] Ошибка в цикле отслеживания: {ex.Message}");
                        await Task.Delay(delayMs, ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }
    }

    public class TrackingStatus
    {
        public string TrackingId { get; set; }
        public int CategoryId { get; set; }
        public int SubcategoryId { get; set; }
        public string Filter { get; set; }
        public DateTime StartedAt { get; set; }
        public int PendingCount { get; set; } // Количество объявлений в очереди, ожидающих отправки бота
    }
}