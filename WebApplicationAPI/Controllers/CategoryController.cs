using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Text;
using System.Text.Json;
using WebApplicationAPI.Models.Category;
using WebApplicationAPI.Services;
namespace WebApplicationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Базовый маршрут для этого контроллера
    public class CategoriesController : ControllerBase
    {
        private readonly CategoryService _categoryService;

        // Внедрение CategoryService через конструктор (Dependency Injection)
        public CategoriesController(CategoryService categoryService)
        {
            _categoryService = categoryService;
        }

        /// <summary>
        /// Получает список всех категорий.
        /// </summary>
        /// <returns>Список категорий.</returns>
        [HttpGet] // Определяет, что этот метод отвечает на GET-запросы
        public ActionResult<IEnumerable<Category>> GetCategories()
        {
            var categories = _categoryService.GetAllCategories();
            if (!categories.Any())
            {
                return NotFound("Категории не найдены."); // Возвращаем 404, если данных нет
            }
            return Ok(categories); // Возвращаем 200 OK со списком категорий
        }

        /// <summary>
        /// Получает категорию по ее ID.
        /// </summary>
        /// <param name="id">ID категории.</param>
        /// <returns>Категория или 404 Not Found.</returns>
        [HttpGet("{id}")] // Маршрут будет выглядеть как /api/categories/{id}
        public ActionResult<Category> GetCategoryById(string id)
        {
            var category = _categoryService.GetCategoryById(id);
            if (category == null)
            {
                return NotFound($"Категория с ID {id} не найдена.");
            }
            return Ok(category);
        }

        /// <summary>
        /// Получает подкатегории для указанной категории по ID.
        /// </summary>
        /// <param name="categoryId">ID родительской категории.</param>
        /// <returns>Список подкатегорий или 404 Not Found.</returns>
        [HttpGet("{categoryId}/subcategories")] // Маршрут /api/categories/{categoryId}/subcategories
        public ActionResult<IEnumerable<Subcategory>> GetSubcategories(string categoryId)
        {
            var subcategories = _categoryService.GetSubcategoriesByCategoryId(categoryId);
            if (!subcategories.Any())
            {
                return NotFound($"Подкатегории для категории с ID {categoryId} не найдены.");
            }
            return Ok(subcategories);
        }
    }
}
