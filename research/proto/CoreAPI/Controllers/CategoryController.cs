using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using CoreAPI.Models;
using CoreAPI.Services;

namespace CoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CategoriesController> _logger;
        private readonly CategoryCacheService _categoryCache;
        private const string WebApiBaseUrl = "https://localhost:7275";

        public CategoriesController(ILogger<CategoriesController> logger, CategoryCacheService categoryCache)
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            _logger = logger;
            _categoryCache = categoryCache;
        }

        /// <summary>
        /// Получает список всех категорий из кэша.
        /// </summary>
        /// <returns>Список категорий.</returns>
        [HttpGet]
        public IActionResult GetCategories()
        {
            try
            {
                _logger.LogInformation("[GET Categories] Получение всех категорий из кэша");

                var categories = _categoryCache.GetAllCategories();

                if (categories.Count == 0)
                {
                    _logger.LogWarning("[GET Categories] Кэш пуст");
                    return Ok(new List<Category>());
                }

                _logger.LogInformation($"[GET Categories] Успешно возвращено категорий: {categories.Count}");

                return Ok(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"[GET Categories] Ошибка при получении категорий. " +
                    $"Сообщение: {ex.Message}. " +
                    $"Тип исключения: {ex.GetType().Name}");

                return StatusCode(500, $"Внутренняя ошибка сервера: {ex.Message}");
            }
        }

        /// <summary>
        /// Получает категорию по ID из кэша.
        /// </summary>
        /// <param name="id">ID категории</param>
        /// <returns>Категория с указанным ID.</returns>
        [HttpGet("{id}")]
        public IActionResult GetCategoryById(string id)
        {
            try
            {
                _logger.LogInformation($"[GET Category by ID] Получение категории из кэша. ID: {id}");

                var category = _categoryCache.GetCategoryById(id);

                if (category == null)
                {
                    _logger.LogWarning($"[GET Category by ID] Категория не найдена. ID: {id}");
                    return NotFound($"Категория с ID {id} не найдена");
                }

                _logger.LogInformation($"[GET Category by ID] Категория успешно найдена. ID: {id}");

                return Ok(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"[GET Category by ID] Ошибка при получении категории. " +
                    $"ID: {id}, Сообщение: {ex.Message}. " +
                    $"Тип исключения: {ex.GetType().Name}");

                return StatusCode(500, $"Внутренняя ошибка сервера: {ex.Message}");
            }
        }
    }
}
