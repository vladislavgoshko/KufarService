using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using TelegramService.BusinessLogic;

namespace TelegramService.Services
{
    internal class CategoryService
    {
        private readonly HttpClient _httpClient;
        private string CoreApiBaseUrl = "http://localhost:5001";

        public CategoryService(ApiSettings apiSettings)
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            CoreApiBaseUrl = apiSettings.BaseUrl;
        }

        public List<Category> GetCategories()
        {
            try
            {
                //Thread.Sleep(5000);
                var response = _httpClient.GetAsync($"{CoreApiBaseUrl}/api/categories").Result;

                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    using (JsonDocument doc = JsonDocument.Parse(content))
                    {
                        var root = doc.RootElement;
                        var categories = new List<Category>();

                        if (root.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in root.EnumerateArray())
                            {
                                if (item.TryGetProperty("id", out var idElement) &&
                                    item.TryGetProperty("name", out var nameElement))
                                {
                                    if (int.TryParse(idElement.GetString(), out var id))
                                    {
                                        var name = nameElement.GetString() ?? "Unknown";
                                        categories.Add(new Category(name, id));
                                    }
                                }
                            }
                        }
                        return categories;
                    }
                }
                else
                {
                    Console.WriteLine($"API returned status code: {response.StatusCode}");
                    return new List<Category>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении категорий: {ex.Message}");
                return new List<Category>();
            }
        }

        public List<Subcategory> GetSubcategories(int categoryId)
        {
            try
            {
                var response = _httpClient.GetAsync($"{CoreApiBaseUrl}/api/categories/{categoryId}").Result;

                if (response.IsSuccessStatusCode)
                {
                    var content = response.Content.ReadAsStringAsync().Result;
                    using (JsonDocument doc = JsonDocument.Parse(content))
                    {
                        var root = doc.RootElement;
                        var subcategories = new List<Subcategory>();

                        if (root.TryGetProperty("subcategories", out var subcategoriesElement) &&
                            subcategoriesElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in subcategoriesElement.EnumerateArray())
                            {
                                if (item.TryGetProperty("id", out var idElement) &&
                                    item.TryGetProperty("name", out var nameElement))
                                {
                                    if (int.TryParse(idElement.GetString(), out var id))
                                    {
                                        var name = nameElement.GetString() ?? "Unknown";
                                        subcategories.Add(new Subcategory(name, id));
                                    }
                                }
                            }
                        }
                        return subcategories;
                    }
                }
                else
                {
                    Console.WriteLine($"API returned status code: {response.StatusCode}");
                    return new List<Subcategory>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении подкатегорий: {ex.Message}");
                return new List<Subcategory>();
            }
        }

        public int GetCategoryIdBySubcategory(int subcategoryId)
        {
            try
            {
                var categories = GetCategories();
                foreach (var category in categories)
                {
                    var subcategories = GetSubcategories(category.Id);
                    var found = subcategories.FirstOrDefault(s => s.Id == subcategoryId);
                    if (found != null)
                    {
                        return category.Id;
                    }
                }
                return -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении ID категории: {ex.Message}");
                return -1;
            }
        }
    }
}
