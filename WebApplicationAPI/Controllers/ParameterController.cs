using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging; // Added for logging
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks; // Important for async methods
using WebApplicationAPI.Models.Parameter;
using WebApplicationAPI.Services;

namespace WebApplicationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Base route: /api/parameters
    public class ParameterController : ControllerBase
    {
        private readonly ParameterService _parameterService;
        private readonly ILogger<ParameterController> _logger; // Added for logging

        public ParameterController(ParameterService parameterService, ILogger<ParameterController> logger)
        {
            _parameterService = parameterService;
            _logger = logger;
        }

        /// <summary>
        /// Получает список категорий, каждая из которых содержит связанные с ней параметры, определенные правилами,
        /// для указанного ID категории. Этот метод объединяет правила и определения параметров.
        /// </summary>
        /// <param name="categoryId">ID категории, для которой нужно получить объединенные параметры.</param>
        /// <returns>Список объектов CategoryWithParameters.</returns>
        [HttpGet("unified/{categoryId}")] // GET /api/parameters/unified/{categoryId}
        public async Task<ActionResult<List<CategoryWithParameters>>> GetUnifiedCategoriesWithParameters(int categoryId)
        {
            try
            {
                var unifiedData = await _parameterService.GetUnifiedCategoriesWithParameters(categoryId);
                if (unifiedData == null || !unifiedData.Any())
                {
                    _logger.LogWarning($"No unified category data and parameters found for ID: {categoryId}");
                    return NotFound($"Объединенные данные категорий и параметров для ID {categoryId} не найдены.");
                }
                return Ok(unifiedData);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"Error getting unified parameters for category ID {categoryId}.");
                return StatusCode(500, "Произошла внутренняя ошибка сервера при получении объединенных параметров.");
            }
        }
        /// <summary>
        /// Получает объединенные параметры для одной конкретной категории или правила подкатегории.
        /// </summary>
        /// <remarks>
        /// Пример запроса для общих параметров:
        /// GET /api/parameters/unifiedcategory/16000
        ///
        /// Пример запроса для параметров с конкретным правилом (например, для типа "Квартиры"):
        /// GET /api/parameters/unifiedcategory/16000?type=Квартиры&someOtherKey=someValue
        /// </remarks>
        /// <param name="categoryId">ID основной категории.</param>
        /// Если не указан (или пустой), будут получены общие параметры для данной категории.</param>
        /// <returns>Объект CategoryWithParameters (200 OK) или 404 Not Found, если правило не найдено.</returns>
        [HttpGet("unifiedcategory/{categoryId}")]
        [ProducesResponseType(typeof(CategoryWithParameters), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetUnifiedCategoryByRule(
            int categoryId)
        {
            try
            {
                // Метод сервиса GetUnifiedCategoryWithParametersByRule корректно обрабатывает
                // null или пустой словарь `rule` как запрос на общие параметры.
                var categoryWithParameters = await _parameterService.GetUnifiedCategoryWithParametersByRule(categoryId);

                if (categoryWithParameters == null)
                {
                    _logger.LogInformation($"No unified category found for ID {categoryId}.");
                    return NotFound($"Unified category parameters for ID {categoryId} with the specified rule not found.");
                }

                return Ok(categoryWithParameters);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"HTTP request error while getting unified category for ID {categoryId}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error communicating with external service: {ex.Message}");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"JSON deserialization error while getting unified category for ID {categoryId}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error processing data: {ex.Message}");
            }
            catch (System.Exception ex) // Используем System.Exception для перехвата всех остальных исключений
            {
                _logger.LogError(ex, $"An unexpected error occurred while getting unified category for ID {categoryId}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, $"An internal server error occurred: {ex.Message}");
            }
        }
        /// <summary>
        /// Получает параметры для конкретной категории по ее ID.
        /// </summary>
        /// <param name="categoryId">ID категории, для которой нужно получить параметры.</param>
        /// <returns>Объект CategoryWithParameters для указанного ID.</returns>
        [HttpGet("{categoryId}")] // GET /api/parameters/{categoryId}
        public async Task<ActionResult<CategoryWithParameters>> GetCategoryParametersById(int categoryId)
        {
            try
            {
                // Note: GetUnifiedCategoriesWithParameters returns a List<CategoryWithParameters>.
                // We fetch all unified data for the categoryId and then try to find a specific one if needed.
                // However, based on your previous service logic, GetUnifiedCategoriesWithParameters(categoryId)
                // already fetches/generates data specific to that categoryId.
                // The `FirstOrDefault` logic with `x.Rule.Values.FirstOrDefault()` seems to imply
                // that you are trying to find a specific rule within the *unified* data that matches the categoryId.
                // If you intend to return just the *single* general category (the one with an empty rule),
                // then filtering based on `categoryId` and `x.Rule.Values.FirstOrDefault()` might be less direct.
                // A common pattern is to return all unified rules, and the client filters.
                //
                // If the goal is to get the "general" parameters for that category ID (where rule is empty/contains only the category ID itself):
                var unifiedData = await _parameterService.GetUnifiedCategoriesWithParameters(categoryId);
                var generalCategoryParameters = unifiedData.FirstOrDefault(x =>
                    (x.Rule == null || !x.Rule.Any()) || // Matches the rule that was "empty" before adding categoryName
                    (x.Rule.Count == 1 && x.Rule.ContainsKey("categoryName") && x.Rule["categoryName"] == categoryId.ToString()) // Or the one we injected categoryName into
                );


                if (generalCategoryParameters == null)
                {
                    _logger.LogWarning($"No specific unified category data found for ID: {categoryId}");
                    return NotFound($"Объединенные данные категории для ID {categoryId} не найдены.");
                }
                return Ok(generalCategoryParameters);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"Error getting category parameters by ID {categoryId}.");
                return StatusCode(500, "Произошла внутренняя ошибка сервера при получении параметров категории по ID.");
            }
        }

        

        /// <summary>
        /// Получает список "общих" параметров для заданной категории.
        /// "Общие" параметры определяются по свойству 'Meta' со значением "set".
        /// </summary>
        /// <param name="categoryId">ID категории, для которой нужно получить общие параметры.</param>
        /// <returns>Список объектов Parameter, у которых свойство Meta равно "set".</returns>
        [HttpGet("general/{categoryId}")] // GET /api/parameters/general/{categoryId}
        public async Task<ActionResult<List<Parameter>>> GetGeneralParameters(int categoryId)
        {
            try
            {
                var generalParameters = await _parameterService.GetGeneralParameters(categoryId);
                if (generalParameters == null || !generalParameters.Any())
                {
                    _logger.LogInformation($"No general parameters found for category ID: {categoryId}.");
                    return NotFound($"Общие параметры для категории ID {categoryId} не найдены.");
                }
                return Ok(generalParameters);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"Error getting general parameters for category ID {categoryId}.");
                return StatusCode(500, "Произошла внутренняя ошибка сервера при получении общих параметров.");
            }
        }
    }
}