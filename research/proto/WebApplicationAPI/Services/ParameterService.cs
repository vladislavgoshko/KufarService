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
        private async Task<Root> FetchParameterDataAsync(int categoryId)
        {
            string requestUrl = $"https://api.kufar.by/taxonomy-proxy/v1/dispatch?routing=web_generalist&application=ad_listing&platform=web&taxonomy-version=1&parent={categoryId}";

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode();

                string jsonString = await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                Root? parameterData = JsonSerializer.Deserialize<Root>(jsonString, options);

                if (parameterData == null)
                {
                    throw new InvalidOperationException($"Некорректные данные получены для категории ID: {categoryId}");
                }

                return parameterData;
            }
            catch (HttpRequestException ex)
            {
                throw new HttpRequestException($"Ошибка HTTP-запроса при загрузке параметров для категории ID {categoryId}: {ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                throw new JsonException($"Ошибка десериализации JSON для категории ID {categoryId}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Произошла непредвиденная ошибка при загрузке параметров для категории ID {categoryId}: {ex.Message}", ex);
            }
        }

        public async Task<Root> GetParametersForCategoryAsync(int categoryId)
        {
            Root parameterData = await FetchParameterDataAsync(categoryId);

            if (parameterData == null || parameterData.Metadata == null || parameterData.Metadata.Parameters == null)
            {
                throw new InvalidOperationException($"Некорректные данные получены для категории ID: {categoryId}");
            }

            //var parametersOrderRefs = parameterData.Metadata.ParametersOrder?.Refs?["0"] ?? new List<string>();

            //if (parameterData.Metadata.Parameters.Refs == null || parameterData.Metadata.Parameters.Rules == null)
            //{
            //    _logger.LogWarning($"Cannot unify categories for ID {parentId}: Parameter data (Refs or Rules) is null after loading.");
            //    return new List<Parameter>();
            //}

            //var parametersMap = parameterData.Metadata.Parameters.Refs;
            //CategoryParameters generalCategory = new CategoryParameters(parentId, new List<Parameter>());

            //foreach (var ruleInfo in parameterData.Metadata.Parameters.Rules ?? Enumerable.Empty<RuleInfo>())
            //{
            //    int _categoryId;
            //    if (ruleInfo.Rule != null && ruleInfo.Rule.TryGetValue("category", out string? _catId))
            //    {
            //        _categoryId = int.TryParse(_catId, out var parsedId) ? parsedId : parentId;
            //    }
            //    else
            //    {
            //        _categoryId = parentId;
            //    }

            //    CategoryParameters category = new CategoryParameters(_categoryId, new List<Parameter>());

            //    if (ruleInfo.Refs != null)
            //    {
            //        foreach (var paramRefId in ruleInfo.Refs)
            //        {
            //            if (parametersMap != null && parametersMap.TryGetValue(paramRefId, out var foundParameter))
            //            {
            //                if (foundParameter.IsType && parameterData.Metadata.Parameters.Rules != null)
            //                {
            //                    var subParametersRules = parameterData.Metadata.Parameters.Rules
            //                        .FindAll(x =>
            //                            x.Rule != null &&
            //                            x.Rule.TryGetValue("category", out var outCategoryId) &&
            //                            outCategoryId == _categoryId.ToString() &&
            //                            x.Rule.TryGetValue(foundParameter.Name, out var outValue)
            //                        );
            //                    foreach (var rule in subParametersRules)
            //                    {
            //                        var valueToFind = rule.Rule.TryGetValue(foundParameter.Name, out var outValue) ? outValue : null;
            //                        var subParam = foundParameter.Values?.FirstOrDefault(x => x.Value == valueToFind);
            //                        if (subParam == null)
            //                        {
            //                            _logger.LogWarning($"No matching sub-parameter value found for parameter '{foundParameter.Name}' (value: '{valueToFind}') in category ID: {_categoryId}.");
            //                            continue;
            //                        }
            //                    }
            //                }

            //                if (!category.Parameters.Any(p => p.VariationId == foundParameter.VariationId))
            //                {
            //                    category.Parameters.Add(foundParameter);
            //                }
            //            }
            //            else
            //            {
            //                _logger.LogWarning($"Parameter reference ID '{paramRefId}' not found in 'refs' dictionary (Category ID: {_categoryId}).");
            //            }
            //        }
            //    }

            //    category.Parameters = category.Parameters
            //        .OrderBy(p => parametersOrderRefs.IndexOf(p.VariationId.ToString()))
            //        .ToList();

            //    // Собираем все параметры в generalCategory
            //    foreach (var param in category.Parameters)
            //    {
            //        if (!generalCategory.Parameters.Any(p => p.VariationId == param.VariationId))
            //        {
            //            generalCategory.Parameters.Add(param);
            //        }
            //    }
            //}


            return parameterData;
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