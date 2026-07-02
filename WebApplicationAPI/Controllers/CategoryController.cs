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
        public async Task<ActionResult<IEnumerable<Category>>> GetCategories()
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            if (!categories.Any())
            {
                return NotFound("Категории не найдены."); // Возвращаем 404, если данных нет
            }
            return Ok(categories); // Возвращаем 200 OK со списком категорий
        }
    }
}
