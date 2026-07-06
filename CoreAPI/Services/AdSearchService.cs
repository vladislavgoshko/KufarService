using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CoreAPI.Models;
using TelegramService;

namespace CoreAPI.Services
{
    /// <summary>
    /// Сервис для поиска объявлений через WebApplicationAPI.
    /// </summary>
    public class AdSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AdSearchService> _logger;
        //private const string SearchApiUrl = "https://localhost:7275/api/SearchAd";
        private string WebApiBaseUrl = "https://localhost:7275";


        public AdSearchService(ILogger<AdSearchService> logger, ApiSettings apiSettings)
        {
            _logger = logger;
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            WebApiBaseUrl = apiSettings.BaseUrl;
        }

        /// <summary>
        /// Получает объявления по ID категории.
        /// </summary>
        public async Task<List<Advertisement>> SearchByCategoryAsync(int categoryId, int size = 10)
        {
            try
            {
                var url = $"{WebApiBaseUrl}/api/SearchAd?size={size}&filters=cat={categoryId}";
                _logger.LogInformation($"[AdSearchService] Поиск объявлений: {url}");

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    _logger.LogInformation($"[AdSearchService] Ответ получен, размер: {content.Length} байт");

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var kufarResponse = JsonSerializer.Deserialize<KufarResponse>(content, options);
                    var ads = kufarResponse?.Ads ?? new List<Advertisement>();

                    _logger.LogInformation($"[AdSearchService] Найдено объявлений: {ads.Count}");
                    return ads;
                }
                else
                {
                    _logger.LogWarning($"[AdSearchService] Ошибка при поиске. Статус: {response.StatusCode}");
                    return new List<Advertisement>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AdSearchService] Исключение: {ex.Message}");
                return new List<Advertisement>();
            }
        }

        /// <summary>
        /// Получает новые объявления, которых еще нет в переданном списке.
        /// </summary>
        public async Task<List<Advertisement>> GetNewAdvertisementsAsync(int categoryId, List<long> existingIds, int size = 10)
        {
            var allAds = await SearchByCategoryAsync(categoryId, size);
            var newAds = allAds.Where(ad => !existingIds.Contains(ad.Id)).ToList();

            _logger.LogInformation($"[AdSearchService] Новых объявлений: {newAds.Count}");
            return newAds;
        }
    }
}
