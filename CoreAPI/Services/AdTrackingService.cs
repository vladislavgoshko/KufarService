using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreAPI.Models;

namespace CoreAPI.Services
{
    /// <summary>
    /// Сервис для управления отслеживанием объявлений.
    /// Может быть использован разными клиентами (Telegram, Web, мобильные приложения).
    /// </summary>
    public class AdTrackingService
    {
        private readonly AdSearchService _adSearchService;
        private readonly ILogger<AdTrackingService> _logger;
        private readonly ConcurrentDictionary<string, ActiveTracking> _activeTrackings;

        private class ActiveTracking
        {
            public int SubcategoryId { get; set; }
            public int CategoryId { get; set; }
            public HashSet<long> SeenAdvertisementIds { get; set; }
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

        /// <summary>
        /// Начинает отслеживание объявлений для клиента.
        /// </summary>
        public async Task StartTrackingAsync(
            string trackingId, 
            int categoryId, 
            int subcategoryId,
            Func<List<Advertisement>, Task> onNewAdvertisementsCallback = null)
        {
            // Если уже отслеживаем, остановим старое отслеживание
            if (_activeTrackings.ContainsKey(trackingId))
            {
                await StopTrackingAsync(trackingId);
            }

            var cts = new CancellationTokenSource();
            var tracking = new ActiveTracking
            {
                SubcategoryId = subcategoryId,
                CategoryId = categoryId,
                SeenAdvertisementIds = new HashSet<long>(),
                CancellationTokenSource = cts,
                StartedAt = DateTime.Now,
                ClientId = trackingId,
                OnNewAdvertisementsCallback = onNewAdvertisementsCallback
            };

            // Получаем начальные объявления
            var initialAds = await _adSearchService.SearchByCategoryAsync(categoryId, 10);
            foreach (var ad in initialAds)
            {
                tracking.SeenAdvertisementIds.Add(ad.Id);
            }

            // Создаем и запускаем фоновую задачу отслеживания
            tracking.TrackingTask = TrackingLoopAsync(tracking, cts.Token);

            _activeTrackings.TryAdd(trackingId, tracking);

            _logger.LogInformation(
                $"[AdTrackingService] Отслеживание начато. TrackingId: {trackingId}, " +
                $"CategoryId: {categoryId}, Subcategory: {subcategoryId}");
        }

        /// <summary>
        /// Останавливает отслеживание для клиента.
        /// </summary>
        public async Task StopTrackingAsync(string trackingId)
        {
            if (_activeTrackings.TryRemove(trackingId, out var tracking))
            {
                tracking.CancellationTokenSource.Cancel();
                try
                {
                    await tracking.TrackingTask;
                }
                catch (OperationCanceledException)
                {
                    // Ожидаемое исключение при отмене
                }

                _logger.LogInformation($"[AdTrackingService] Отслеживание остановлено. TrackingId: {trackingId}");
            }
        }

        /// <summary>
        /// Возвращает список всех активных отслеживаний.
        /// </summary>
        public Dictionary<string, TrackingStatus> GetActiveTrackings()
        {
            return _activeTrackings.ToDictionary(
                kvp => kvp.Key,
                kvp => new TrackingStatus
                {
                    TrackingId = kvp.Key,
                    CategoryId = kvp.Value.CategoryId,
                    SubcategoryId = kvp.Value.SubcategoryId,
                    StartedAt = kvp.Value.StartedAt,
                    SeenCount = kvp.Value.SeenAdvertisementIds.Count
                }
            );
        }

        /// <summary>
        /// Получает статус отслеживания по ID.
        /// </summary>
        public TrackingStatus GetTrackingStatus(string trackingId)
        {
            if (_activeTrackings.TryGetValue(trackingId, out var tracking))
            {
                return new TrackingStatus
                {
                    TrackingId = trackingId,
                    CategoryId = tracking.CategoryId,
                    SubcategoryId = tracking.SubcategoryId,
                    StartedAt = tracking.StartedAt,
                    SeenCount = tracking.SeenAdvertisementIds.Count
                };
            }

            return null;
        }

        /// <summary>
        /// Получает новые объявления для отслеживания по ID трекинга.
        /// </summary>
        public async Task<List<Advertisement>> GetNewAdvertisementsAsync(string trackingId)
        {
            if (!_activeTrackings.TryGetValue(trackingId, out var tracking))
            {
                _logger.LogWarning($"[AdTrackingService] Отслеживание не найдено. TrackingId: {trackingId}");
                return new List<Advertisement>();
            }

            try
            {
                var newAds = await _adSearchService.GetNewAdvertisementsAsync(
                    tracking.SubcategoryId,
                    tracking.SeenAdvertisementIds.ToList(),
                    10
                );

                // Добавляем найденные объявления в список просмотренных
                foreach (var ad in newAds)
                {
                    tracking.SeenAdvertisementIds.Add(ad.Id);
                }

                return newAds;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AdTrackingService] Ошибка при получении новых объявлений: {ex.Message}");
                return new List<Advertisement>();
            }
        }

        /// <summary>
        /// Основной цикл отслеживания объявлений.
        /// </summary>
        private async Task TrackingLoopAsync(ActiveTracking tracking, CancellationToken ct)
        {
            const int intervalSeconds = 5;
            var delayMs = intervalSeconds * 1000;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Получаем новые объявления
                        var newAds = await _adSearchService.GetNewAdvertisementsAsync(
                            tracking.SubcategoryId,
                            tracking.SeenAdvertisementIds.ToList(),
                            10
                        );

                        // Если есть новые объявления
                        if (newAds.Count > 0)
                        {
                            _logger.LogInformation(
                                $"[AdTrackingService] Новых объявлений: {newAds.Count} " +
                                $"для TrackingId: {tracking.ClientId}");

                            // Добавляем ID в список просмотренных
                            foreach (var ad in newAds)
                            {
                                tracking.SeenAdvertisementIds.Add(ad.Id);
                            }

                            // Вызываем callback если он установлен
                            if (tracking.OnNewAdvertisementsCallback != null)
                            {
                                try
                                {
                                    await tracking.OnNewAdvertisementsCallback(newAds);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(
                                        $"[AdTrackingService] Ошибка при вызове callback: {ex.Message}");
                                }
                            }
                        }

                        // Ожидаем перед следующей проверкой
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
            catch (OperationCanceledException)
            {
                // Нормальное завершение при отмене
            }
        }
    }

    public class TrackingStatus
    {
        public string TrackingId { get; set; }
        public int CategoryId { get; set; }
        public int SubcategoryId { get; set; }
        public DateTime StartedAt { get; set; }
        public int SeenCount { get; set; }
    }
}
