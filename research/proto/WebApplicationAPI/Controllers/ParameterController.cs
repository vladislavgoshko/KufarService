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
        /// Получает параметры для конкретной категории по ее ID.
        /// </summary>
        /// <param name="categoryId">ID категории, для которой нужно получить параметры.</param>
        /// <returns>Объект CategoryWithParameters для указанного ID.</returns>
        [HttpGet("{categoryId}")] // GET /api/parameters/{categoryId}
        public async Task<ActionResult<List<Parameter>>> GetCategoryParametersById(int categoryId)
        {
            try
            {

                var parameters = await _parameterService.GetParametersForCategoryAsync(categoryId);



                if (parameters == null)
                {
                    return NotFound($"Объединенные данные категории для ID {categoryId} не найдены.");
                }
                return Ok(parameters);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, "Произошла внутренняя ошибка сервера при получении параметров категории по ID.");
            }
        }
    }
}