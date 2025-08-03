using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using WebApplicationAPI.Models.Parameter;
using System.Linq;

namespace WebApplicationAPI.Services
{
    public class ParameterService
    {
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ParameterService> _logger;

        public ParameterService(HttpClient httpClient, IWebHostEnvironment env, ILogger<ParameterService> logger)
        {
            _httpClient = httpClient;
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// Загружает параметры для указанной категории по ID из интернета.
        /// </summary>
        /// <param name="categoryId">ID категории, для которой нужно загрузить параметры.</param>
        /// <returns>Список параметров для данной категории.</returns>
        /// <exception cref="HttpRequestException">Вызывается при ошибке HTTP-запроса.</exception>
        /// <exception cref="JsonException">Вызывается при ошибке десериализации JSON.</exception>
        /// <exception cref="InvalidOperationException">Вызывается, если полученные данные некорректны.</exception>
        public async Task<List<Parameter>> GetParametersForCategoryAsync(int categoryId)
        {
            int parentId = Tools.CategoryTools.GetParentCategoryId(categoryId);

            _logger.LogInformation($"Loading parameters from external API for category ID: {categoryId}");

            string requestUrl = $"https://api.kufar.by/taxonomy-proxy/v1/dispatch?routing=web_generalist&application=ad_listing&platform=web&taxonomy-version=1&parent={parentId}";

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode(); // Выбросит исключение для кодов состояния HTTP 4xx или 5xx

                string jsonString = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Successfully fetched data for category ID {categoryId}. Content length: {jsonString.Length}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                Root? parameterData = JsonSerializer.Deserialize<Root>(jsonString, options);

                var parametersOrderRefs = parameterData.Metadata.ParametersOrder.Refs["0"];

                if (parameterData == null || parameterData.Metadata == null || parameterData.Metadata.Parameters == null)
                {
                    _logger.LogError($"Deserialized parameter data for category ID {categoryId} is incomplete (null Root, Metadata, or Parameters).");
                    throw new InvalidOperationException($"Некорректные данные получены для категории ID: {categoryId}");
                }

                if (parameterData?.Metadata?.Parameters?.Refs == null || parameterData.Metadata.Parameters.Rules == null)
                {
                    _logger.LogWarning($"Cannot unify categories for ID {categoryId}: Parameter data (Refs or Rules) is null after loading.");
                }

                _logger.LogInformation($"Starting unification process for category ID {categoryId}. Rules count: {parameterData?.Metadata?.Parameters?.Rules?.Count}");
                var parametersMap = parameterData?.Metadata.Parameters.Refs;

                CategoryParameters generalCategory = new CategoryParameters(parentId, new List<Parameter>());

                foreach (var ruleInfo in parameterData.Metadata.Parameters.Rules)
                {
                    CategoryParameters category;

                    // If rule without category value writing it in parent category
                    int _categoryId;
                    if (ruleInfo.Rule.TryGetValue("category", out string? _catId))
                    {
                        _categoryId = int.Parse(_catId);
                    }
                    else
                    {
                        _categoryId = parentId;
                    }

                    category = new CategoryParameters(_categoryId, new List<Parameter>());

                    if (ruleInfo.Refs != null)
                    {
                        foreach (var paramRefId in ruleInfo.Refs)
                        {
                            if (parametersMap.TryGetValue(paramRefId, out var foundParameter))
                            {
                                // Linking subparameters with types
                                if (foundParameter.IsType)
                                {
                                    var subParametersRules = parameterData.Metadata.Parameters.Rules.
                                        FindAll(x =>
                                        x.Rule.TryGetValue("category", out var outCategoryId) &&
                                        outCategoryId == _categoryId.ToString() &&
                                        x.Rule.TryGetValue(foundParameter.Name, out var outValue)
                                        );
                                    foreach (var rule in subParametersRules)
                                    {
                                        foundParameter.Values
                                            .First(x =>
                                            x.Value == (rule.Rule.TryGetValue(foundParameter.Name, out var outValue) ? outValue : null));
                                    }
                                }

                                if (!category.Parameters.Any(p => p.VariationId == foundParameter.VariationId))
                                {
                                    category.Parameters.Add(foundParameter);
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"Parameter reference ID '{paramRefId}' not found in 'refs' dictionary (Category ID: {_categoryId}).");
                            }
                        }
                    }

                    category.Parameters = category.Parameters
                        .OrderBy(p => parametersOrderRefs.IndexOf(p.VariationId.ToString()))
                        .ToList();
                }

                _logger.LogInformation($"Parameters for category ID: {categoryId} successfully loaded.");

                return generalCategory.Parameters;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"HTTP request error when loading parameters for category ID {categoryId}: {ex.Message}");
                throw new HttpRequestException($"Ошибка HTTP-запроса при загрузке параметров для категории ID {categoryId}: {ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"JSON deserialization error when loading parameters for category ID {categoryId}: {ex.Message}");
                throw new JsonException($"Ошибка десериализации JSON для категории ID {categoryId}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An unexpected error occurred while loading parameters for category ID {categoryId}: {ex.Message}");
                throw new InvalidOperationException($"Произошла непредвиденная ошибка при загрузке параметров для категории ID {categoryId}: {ex.Message}", ex);
            }
        }
        private class CategoryParameters
        {
            public int Id { get; set; }
            public List<Parameter> Parameters { get; set; }
            public CategoryParameters(int id, List<Parameter> parameters)
            {
                Id = id;
                Parameters = parameters;
            }
        }
    }
}