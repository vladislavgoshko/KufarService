using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using WebApplicationAPI.Models.Ad;
using WebApplicationAPI.Models.SearchAd;

namespace WebApplicationAPI.Services
{
    public class SearchAdService
    {
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SearchAdService> _logger;


        public SearchAdService(HttpClient httpClient, IWebHostEnvironment env, ILogger<SearchAdService> logger)
        {
            _httpClient = httpClient;
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// Получает объявления по указанной категории с API Kufar
        /// </summary>
        public async Task<KufarResponse> SearchAdsAsync(int? size, string? cursor, string? filters)
        {
            string url = $"https://api.kufar.by/search-api/v2/search/rendered-paginated?" +
                (size == null ? "" : $"size={size}") +
                (cursor == null ? "" : $"cursor={cursor}") +
                $"&{filters}" +
                $"&sort=lst.d"
                ;
            _logger.LogInformation("URL: " + url);
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Fetched ads. JSON length: {json.Length}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var kufarResponse = JsonSerializer.Deserialize<KufarResponse>(json, options);

                if (kufarResponse == null || kufarResponse.Ads == null)
                {
                    throw new InvalidOperationException("Некорректный ответ от сервера Kufar");
                }

                var ads = kufarResponse.Ads;


                return kufarResponse;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"Ошибка HTTP при получении объявлений");
                throw;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"Ошибка десериализации JSON при получении объявлений");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Непредвиденная ошибка при получении объявлений");
                throw;
            }
        }

        private class CachedAds
        {
            public List<Ad> Ads { get; set; } = new();
            public DateTimeOffset LastUpdated { get; set; }
        }
    }

}
