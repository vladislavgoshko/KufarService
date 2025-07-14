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
using System.Linq; // Ensure this is present for .Any() and .Where()

namespace WebApplicationAPI.Services
{
    public class ParameterService
    {
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ParameterService> _logger;

        // Словарь для хранения данных по категориям с кэшированием и временем последнего обновления
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, CategoryParameters>> _cachedCategoryParameters;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        public ParameterService(HttpClient httpClient, IWebHostEnvironment env, ILogger<ParameterService> logger)
        {
            _httpClient = httpClient;
            _env = env;
            _logger = logger;
            _cachedCategoryParameters = new ConcurrentDictionary<int, ConcurrentDictionary<int, CategoryParameters>>();
        }

        /// <summary>
        /// Загружает параметры для указанной категории по ID из интернета,
        /// кэширует их и обновляет каждые 24 часа.
        /// </summary>
        /// <param name="categoryId">ID категории, для которой нужно загрузить параметры.</param>
        /// <returns>Список параметров для данной категории.</returns>
        /// <exception cref="HttpRequestException">Вызывается при ошибке HTTP-запроса.</exception>
        /// <exception cref="JsonException">Вызывается при ошибке десериализации JSON.</exception>
        /// <exception cref="InvalidOperationException">Вызывается, если полученные данные некорректны.</exception>
        public async Task<List<Parameter>> GetParametersForCategoryAsync(int categoryId)
        {
            int parentId = GetParentCategoryId(categoryId);
            // Проверяем, есть ли данные в кэше и не устарели ли они
            if (_cachedCategoryParameters.TryGetValue(parentId, out var cachedData))
            {
                if (DateTimeOffset.UtcNow - cachedData[parentId].LastUpdated < CacheDuration)
                {
                    _logger.LogInformation($"Using cached parameters for category ID: {categoryId}");
                    return cachedData[categoryId].Parameters;
                }
                else
                {
                    _logger.LogInformation($"Cached parameters for category ID: {categoryId} are outdated. Refreshing...");
                }
            }

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

                _cachedCategoryParameters[parentId] = new ConcurrentDictionary<int, CategoryParameters>();

                CategoryParameters generalCategory = new CategoryParameters(parentId, new List<Parameter>());

                _cachedCategoryParameters[parentId][parentId] = generalCategory;

                foreach (var ruleInfo in parameterData.Metadata.Parameters.Rules)
                {
                    CategoryParameters category;

                    // If rule without category value wrinting it in parent category
                    int _categoryId;
                    if (ruleInfo.Rule.TryGetValue("category", out string? _catId))
                    {
                        _categoryId = int.Parse(_catId);
                    }
                    else
                    {
                        _categoryId = parentId;
                    }

                    // Creating new CategoryParameters or getting existing
                    if (_cachedCategoryParameters[parentId].TryGetValue(_categoryId, out var categoryParameters))
                    {
                        category = categoryParameters;
                    }
                    else
                    {
                        category = new CategoryParameters(_categoryId, new List<Parameter>());
                        _cachedCategoryParameters[parentId][_categoryId] = category;
                    }


                    if (ruleInfo.Refs != null)
                    {
                        foreach (var paramRefId in ruleInfo.Refs)
                        {
                            if (parametersMap.TryGetValue(paramRefId, out var foundParameter))
                            {
                                // Linking subparameters with types
                                if (foundParameter.IsType)
                                {
                                    var subRapametersRules = parameterData.Metadata.Parameters.Rules.
                                        FindAll(x =>
                                        x.Rule.TryGetValue("category", out var outCategoryId) &&
                                        outCategoryId == _categoryId.ToString() &&
                                        x.Rule.TryGetValue(foundParameter.Name, out var outValue)
                                        );
                                    foreach (var rule in subRapametersRules)
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


                _logger.LogInformation($"Parameters for category ID: {categoryId} successfully loaded and cached.");

                return _cachedCategoryParameters[parentId][categoryId].Parameters;
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

        /// <summary>
        /// Combines the parameter rules with the actual parameter definitions
        /// to create a list of categories with their associated parameters.
        /// This method now requires a categoryId to fetch the specific data.
        /// </summary>
        /// <param name="categoryId">The ID of the category to get unified parameters for.</param>
        /// <returns>A list of CategoryWithParameters objects.</returns>
        //public async Task<List<CategoryWithParameters>> GetUnifiedCategoriesWithParameters(int categoryId)
        //{
        //    var unifiedCategories = new List<CategoryWithParameters>();
        //    Root parameterData = await GetParametersForCategoryAsync(categoryId); // Fetch or get from cache

        //    if (parameterData?.Metadata?.Parameters?.Refs == null || parameterData.Metadata.Parameters.Rules == null)
        //    {
        //        _logger.LogWarning($"Cannot unify categories for ID {categoryId}: Parameter data (Refs or Rules) is null after loading.");
        //        return unifiedCategories;
        //    }

        //    _logger.LogInformation($"Starting unification process for category ID {categoryId}. Rules count: {parameterData.Metadata.Parameters.Rules.Count}");
        //    var parametersMap = parameterData.Metadata.Parameters.Refs;

        //    foreach (var ruleInfo in parameterData.Metadata.Parameters.Rules)
        //    {
        //        var category = new CategoryWithParameters
        //        {
        //            Rule = ruleInfo.Rule ?? new Dictionary<string, string>(), // Ensure Rule is not null
        //            Parameters = new List<Parameter>()
        //        };

        //        if (ruleInfo.Rule != null && ruleInfo.Rule.Any())
        //        {
        //            category.CategoryName = string.Join(", ", ruleInfo.Rule.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
        //        }
        //        else
        //        {
        //            category.CategoryName = "Общие параметры";
        //        }

        //        if (ruleInfo.Refs != null)
        //        {
        //            foreach (var paramRefId in ruleInfo.Refs)
        //            {
        //                if (parametersMap.TryGetValue(paramRefId, out var foundParameter))
        //                {
        //                    if (!category.Parameters.Any(p => p.VariationId == foundParameter.VariationId))
        //                    {
        //                        category.Parameters.Add(foundParameter);
        //                    }
        //                }
        //                else
        //                {
        //                    _logger.LogWarning($"Parameter reference ID '{paramRefId}' not found in 'refs' dictionary for rule: {category.CategoryName} (Category ID: {categoryId}).");
        //                }
        //            }
        //        }
        //        unifiedCategories.Add(category);
        //    }

        //    // This specific line seems to be a hardcoded fix from the original code.
        //    // If it's intended to always add the categoryId to a specific rule, keep it.
        //    // Otherwise, it might need to be re-evaluated based on the actual logic.
        //    // For now, I'm adapting it to the new structure by trying to find the "general" rule.
        //    var generalCategory = unifiedCategories.FirstOrDefault(x => x.Rule == null || !x.Rule.Any());
        //    if (generalCategory != null && !generalCategory.Rule.ContainsKey("categoryName"))
        //    {
        //        generalCategory.Rule.Add("categoryName", categoryId.ToString());
        //    }
        //    else if (generalCategory == null)
        //    {
        //        // If there's no general category, you might want to create one or handle this case differently
        //        _logger.LogWarning($"No 'general' category rule found for category ID {categoryId} to add 'categoryName'.");
        //    }


        //    _logger.LogInformation($"Unification complete for category ID {categoryId}. Unified categories count: {unifiedCategories.Count}");
        //    return unifiedCategories;
        //}
        /// <summary>
        /// Finds and combines parameter rules with their definitions for a specific category rule.
        /// </summary>
        /// <param name="categoryId">The ID of the main category.</param>
        /// <param name="categoryRule">A dictionary representing the specific category rule (e.g., {"type": "Квартиры"}).
        /// Pass an empty dictionary or null for the "general" category rule.</param>
        /// <returns>A CategoryWithParameters object if a matching rule is found, otherwise null.</returns>
        //public async Task<CategoryWithParameters?> GetUnifiedCategoryWithParametersByRule(int categoryId)
        //{
        //    var parameterData = await GetUnifiedCategoriesWithParameters(categoryId / 1000 * 1000);
        //    Root parameterData2 = await GetParametersForCategoryAsync(categoryId / 1000 * 1000);
        //    var parametersOrderRefs = parameterData2.Metadata.ParametersOrder.Refs["0"];

        //    var categoryWithParameters = parameterData.First(x => x.Rule.First().Value == categoryId.ToString());
        //    //categoryWithParameters.Parameters.OrderBy(p => parametersOrderRefs.IndexOf(p.VariationId.ToString()));

        //    categoryWithParameters.Parameters = categoryWithParameters.Parameters
        //        .OrderBy(p => parametersOrderRefs.IndexOf(p.VariationId.ToString()))
        //        .ToList();

        //    return categoryWithParameters;
        //}
        /// <summary>
        /// Gets the dictionary of all parameters referenced by their IDs for a specific category.
        /// </summary>
        /// <param name="categoryId">The ID of the category.</param>
        /// <returns>A dictionary where keys are parameter IDs and values are Parameter objects.</returns>
        //public async Task<Dictionary<string, Parameter>> GetAllParameterRefs(int categoryId)
        //{
        //    Root parameterData = await GetParametersForCategoryAsync(categoryId);
        //    return parameterData?.Metadata?.Parameters?.Refs ?? new Dictionary<string, Parameter>();
        //}

        /// <summary>
        /// Gets a specific parameter by its reference ID for a specific category.
        /// </summary>
        /// <param name="categoryId">The ID of the category.</param>
        /// <param name="paramRefId">The ID of the parameter reference.</param>
        /// <returns>The Parameter object if found, otherwise null.</returns>
        //public async Task<Parameter?> GetParameterByRefId(int categoryId, string paramRefId)
        //{
        //    Root parameterData = await GetParametersForCategoryAsync(categoryId);
        //    if (parameterData?.Metadata?.Parameters?.Refs != null &&
        //        parameterData.Metadata.Parameters.Refs.TryGetValue(paramRefId, out var parameter))
        //    {
        //        return parameter;
        //    }
        //    return null;
        //}

        /// <summary>
        /// Возвращает список "общих" параметров для заданной категории.
        /// "Общие" параметры определяются по свойству 'Meta' со значением "set".
        /// </summary>
        /// <param name="categoryId">ID категории, для которой нужно получить общие параметры.</param>
        /// <returns>Список объектов Parameter, у которых свойство Meta равно "set".</returns>
        //public async Task<List<Parameter>> GetGeneralParameters(int categoryId)
        //{
        //    //List<string> excludeList = ["area", "region", "category", "parent_category"];

        //    Root parameterData = await GetParametersForCategoryAsync(categoryId);

        //    if (parameterData?.Metadata?.Parameters?.Refs == null)
        //    {
        //        _logger.LogWarning($"Cannot get general parameters for category ID {categoryId}: Parameter Refs data is null.");
        //        return new List<Parameter>();
        //    }

        //    var generalParameters = parameterData.Metadata.Parameters.Refs.Values
        //        .ToList();

        //    _logger.LogInformation($"Found {generalParameters.Count} general parameters for category ID: {categoryId} (excluding 'area').");


        //    var parametersOrderRefs = parameterData.Metadata.ParametersOrder.Refs["0"];

        //    return generalParameters
        //        .OrderBy(p => parametersOrderRefs.IndexOf(p.VariationId.ToString()))
        //        .ToList();
        //}

        // Вспомогательный класс для кэширования данных по категориям
        private class CategoryParameters
        {
            public int Id { get; set; }
            public DateTimeOffset LastUpdated { get; set; }
            public List<Parameter> Parameters { get; set; }
            public CategoryParameters(int id, List<Parameter> parameters)
            {
                Id = id;
                Parameters = parameters;
                LastUpdated = DateTimeOffset.Now;
            }
        }
        /// <summary>
        /// Calculates the parent ID for a given category ID by truncating it to the nearest thousand.
        /// E.g., 1234 returns 1000, 567 returns 0.
        /// </summary>
        /// <param name="categoryId">The category ID.</param>
        /// <returns>The parent ID.</returns>
        public static int GetParentCategoryId(int categoryId)
        {
            return categoryId / 1000 * 1000;
        }
    }
}