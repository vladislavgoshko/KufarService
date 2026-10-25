using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using WebApplicationAPI.Models.Category; // Required for OrderBy, ToList

namespace WebApplicationAPI.Services
{
    public class CategoryService
    {
        private CategoryData _categoryData;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _httpClientFactory;

        public CategoryService(IWebHostEnvironment env, IHttpClientFactory httpClientFactory)
        {
            _env = env;
            _httpClientFactory = httpClientFactory;
        }

        private async Task<string> FetchCategoryDataJsonAsync()
        {
            string url = "https://api.kufar.by/category-tree/v1/category_tree";
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<List<Category>> GetAllCategoriesAsync()
        {
            try
            {
                var jsonString = await FetchCategoryDataJsonAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                _categoryData = JsonSerializer.Deserialize<CategoryData>(jsonString, options);

                if (_categoryData?.Categories != null)
                {
                    _categoryData.Categories = _categoryData.Categories.OrderBy(c => c.Id).ToList();

                    foreach (var category in _categoryData.Categories)
                    {
                        if (category.Subcategories != null)
                        {
                            category.Subcategories = category.Subcategories.OrderBy(s => s.Id).ToList();
                        }
                    }
                }

                return _categoryData?.Categories?
                    .OrderBy(c => int.TryParse(c.Order, out var order) ? order : int.MaxValue)
                    .Select(c =>
                    {
                        c.Subcategories = c.Subcategories?
                            .OrderBy(sc => int.TryParse(sc.Order, out var order) ? order : int.MaxValue)
                            .ToList() ?? new List<Subcategory>();
                        return c;
                    })
                    .ToList() ?? new List<Category>();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Ошибка при десериализации файла категорий: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Произошла ошибка при загрузке категорий: {ex.Message}", ex);
            }
        }
    }
}