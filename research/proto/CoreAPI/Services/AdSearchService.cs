using CoreAPI.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TelegramService;

namespace CoreAPI.Services
{
    public class AdSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AdSearchService> _logger;
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
        /// Получает объявления по ID категории и опциональному фильтру.
        /// </summary>
        public async Task<List<Advertisement>> SearchByCategoryAsync(int categoryId, string filter = null, int size = 10)
        {
            try
            {
                // Формируем базовый URL
                var url = $"{WebApiBaseUrl}/api/SearchAd?size={size}&filters=cat={categoryId}";

                // Если передан дополнительный фильтр (например, "ar=7"), добавляем его через разделитель ';'
                if (!string.IsNullOrEmpty(filter))
                {
                    url += $";{filter}";
                }

                _logger.LogInformation($"[AdSearchService] Поиск объявлений: {url}");

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

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
    }
}