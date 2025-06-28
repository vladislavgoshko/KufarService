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

        public CategoryService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task LoadCategoriesAsync()
        {
            string filePath = Path.Combine(_env.ContentRootPath, "categories.json");

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Файл категорий не найден: {filePath}");
            }

            string jsonString = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            try
            {
                _categoryData = JsonSerializer.Deserialize<CategoryData>(jsonString, options);

                // --- IMPORTANT: SORTING LOGIC ADDED HERE ---

                if (_categoryData?.Categories != null)
                {
                    // Sort the main categories by Id (string comparison)
                    _categoryData.Categories = _categoryData.Categories.OrderBy(c => c.Id).ToList();

                    // Sort subcategories within each category by Id (string comparison)
                    foreach (var category in _categoryData.Categories)
                    {
                        if (category.Subcategories != null)
                        {
                            category.Subcategories = category.Subcategories.OrderBy(s => s.Id).ToList();
                        }
                    }
                }

                // --- END SORTING LOGIC ---

            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Ошибка при десериализации файла категорий: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Произошла ошибка при загрузке файла категорий: {ex.Message}", ex);
            }
        }

        public List<Category> GetAllCategories()
        {
            return _categoryData?.Categories?.ToList() ?? new List<Category>();
        }

        public Category? GetCategoryById(string id)
        {
            return _categoryData?.Categories?.FirstOrDefault(c => c.Id == id);
        }

        public List<Subcategory> GetSubcategoriesByCategoryId(string categoryId)
        {
            return _categoryData?.Categories?.FirstOrDefault(c => c.Id == categoryId)
                                 ?.Subcategories?.ToList() ?? new List<Subcategory>();
        }
    }
}