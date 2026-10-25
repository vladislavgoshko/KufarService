using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebApplicationAPI.Models.Ad;
using WebApplicationAPI.Services;

namespace WebApplicationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Base route: /api/ad
    public class SearchAdController : ControllerBase
    {
        private readonly SearchAdService _adService;
        private readonly ILogger<SearchAdController> _logger;

        public SearchAdController(SearchAdService adService, ILogger<SearchAdController> logger)
        {
            _adService = adService;
            _logger = logger;
        }

        /// <summary>
        /// Получает список объявлений с Kufar API.
        /// </summary>
        /// <returns>Список объявлений.</returns>
        [HttpGet] // GET /api/ad/{categoryId}

        public async Task<ActionResult<List<Ad>>> SearchAds(int? size, string? cursor, string? filters)
        {
            try
            {
                var adsPage = await _adService.SearchAdsAsync(size, cursor, filters);

                if (adsPage == null || (adsPage.Ads?.Count) == 0)
                {
                    return NotFound("Объявления не найдены.");
                }

                return Ok(adsPage);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, "Произошла внутренняя ошибка сервера при получении объявлений.");
            }
        }
    }
}
