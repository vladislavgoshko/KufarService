using System.Text.Json;
using CoreAPI.Models;

namespace CoreAPI.Services
{
    /// <summary>
    /// Сервис для кэширования всех категорий из WebApplicationAPI.
    /// Загружает категории при инициализации и хранит их в памяти для быстрого доступа.
    /// </summary>
    public class CategoryCacheService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CategoryCacheService> _logger;
        private List<Category> _categoriesCache;
        private const string WebApiBaseUrl = "https://localhost:7275";

        public CategoryCacheService(ILogger<CategoryCacheService> logger)
        {
            _logger = logger;
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            _categoriesCache = new List<Category>();
        }

        /// <summary>
        /// Загружает все категории из WebApplicationAPI и кэширует их.
        /// </summary>
        public async Task InitializeAsync()
        {
            var requestUrl = $"{WebApiBaseUrl}/api/categories";
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation($"[CategoryCacheService] Инициализация кэша категорий. Запрос к {requestUrl}");

                var response = await _httpClient.GetAsync(requestUrl);
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var categories = JsonSerializer.Deserialize<List<Category>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _categoriesCache = categories ?? new List<Category>();

                    _logger.LogInformation(
                        $"[CategoryCacheService] Кэш успешно инициализирован. " +
                        $"Загружено категорий: {_categoriesCache.Count}, " +
                        $"Время загрузки: {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    _logger.LogWarning(
                        $"[CategoryCacheService] Ошибка при загрузке категорий. " +
                        $"Статус: {response.StatusCode}, Время: {stopwatch.ElapsedMilliseconds}ms");

                    _categoriesCache = new List<Category>();
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    $"[CategoryCacheService] Исключение при инициализации кэша. " +
                    $"Сообщение: {ex.Message}, Тип: {ex.GetType().Name}, " +
                    $"Время до ошибки: {stopwatch.ElapsedMilliseconds}ms");

                _categoriesCache = new List<Category>();
            }
        }

        /// <summary>
        /// Получает все категории из кэша.
        /// </summary>
        public List<Category> GetAllCategories()
        {
            return _categoriesCache;
        }

        /// <summary>
        /// Получает категорию по ID из кэша.
        /// </summary>
        public Category GetCategoryById(string id)
        {
            var category = _categoriesCache.FirstOrDefault(c => c.Id == id);

            if (category != null)
            {
                _logger.LogInformation($"[CategoryCacheService] Категория найдена в кэше. ID: {id}");
            }
            else
            {
                _logger.LogWarning($"[CategoryCacheService] Категория не найдена в кэше. ID: {id}");
            }

            return category;
        }

        /// <summary>
        /// Получает подкатегорию по ID категории и ID подкатегории.
        /// </summary>
        public Subcategory GetSubcategoryById(string categoryId, string subcategoryId)
        {
            var category = GetCategoryById(categoryId);
            if (category == null)
            {
                _logger.LogWarning(
                    $"[CategoryCacheService] Категория не найдена для поиска подкатегории. " +
                    $"CategoryID: {categoryId}, SubcategoryID: {subcategoryId}");
                return null;
            }

            var subcategory = category.Subcategories?.FirstOrDefault(s => s.Id == subcategoryId);

            if (subcategory != null)
            {
                _logger.LogInformation(
                    $"[CategoryCacheService] Подкатегория найдена в кэше. " +
                    $"CategoryID: {categoryId}, SubcategoryID: {subcategoryId}");
            }
            else
            {
                _logger.LogWarning(
                    $"[CategoryCacheService] Подкатегория не найдена в кэше. " +
                    $"CategoryID: {categoryId}, SubcategoryID: {subcategoryId}");
            }

            return subcategory;
        }
    }
}
