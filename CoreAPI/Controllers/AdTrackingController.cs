using Microsoft.AspNetCore.Mvc;
using CoreAPI.Models;
using CoreAPI.Services;

namespace CoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdTrackingController : ControllerBase
    {
        private readonly AdTrackingService _adTrackingService;
        private readonly ILogger<AdTrackingController> _logger;

        public AdTrackingController(AdTrackingService adTrackingService, ILogger<AdTrackingController> logger)
        {
            _adTrackingService = adTrackingService;
            _logger = logger;
        }

        /// <summary>
        /// Начинает отслеживание объявлений по подкатегории.
        /// </summary>
        /// <param name="request">Данные для начала отслеживания</param>
        [HttpPost("start")]
        public async Task<IActionResult> StartTracking([FromBody] StartTrackingRequest request)
        {
            if (string.IsNullOrEmpty(request.TrackingId))
            {
                return BadRequest("TrackingId не может быть пустым");
            }

            if (request.CategoryId <= 0 || request.SubcategoryId <= 0)
            {
                return BadRequest("CategoryId и SubcategoryId должны быть больше 0");
            }

            try
            {
                _logger.LogInformation(
                    $"[AdTrackingController] Начало отслеживания. " +
                    $"TrackingId: {request.TrackingId}, CategoryId: {request.CategoryId}, " +
                    $"SubcategoryId: {request.SubcategoryId}");

                await _adTrackingService.StartTrackingAsync(
                    request.TrackingId,
                    request.CategoryId,
                    request.SubcategoryId
                );

                return Ok(new
                {
                    success = true,
                    message = "Отслеживание начато",
                    trackingId = request.TrackingId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AdTrackingController] Ошибка при начале отслеживания: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Останавливает отслеживание объявлений.
        /// </summary>
        [HttpPost("stop")]
        public async Task<IActionResult> StopTracking([FromBody] StopTrackingRequest request)
        {
            if (string.IsNullOrEmpty(request.TrackingId))
            {
                return BadRequest("TrackingId не может быть пустым");
            }

            try
            {
                _logger.LogInformation($"[AdTrackingController] Остановка отслеживания. TrackingId: {request.TrackingId}");

                await _adTrackingService.StopTrackingAsync(request.TrackingId);

                return Ok(new
                {
                    success = true,
                    message = "Отслеживание остановлено",
                    trackingId = request.TrackingId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AdTrackingController] Ошибка при остановке отслеживания: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Получает список всех активных отслеживаний.
        /// </summary>
        [HttpGet("active")]
        public IActionResult GetActiveTrackings()
        {
            try
            {
                var trackings = _adTrackingService.GetActiveTrackings();

                _logger.LogInformation($"[AdTrackingController] Активных отслеживаний: {trackings.Count}");

                return Ok(new
                {
                    success = true,
                    count = trackings.Count,
                    trackings = trackings.Values
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AdTrackingController] Ошибка при получении активных отслеживаний: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Получает новые объявления для отслеживания.
        /// </summary>
        [HttpGet("new-ads/{trackingId}")]
        public async Task<IActionResult> GetNewAdvertisements(string trackingId)
        {
            if (string.IsNullOrEmpty(trackingId))
            {
                return BadRequest("TrackingId не может быть пустым");
            }

            try
            {
                _logger.LogInformation($"[AdTrackingController] Получение новых объявлений. TrackingId: {trackingId}");

                var newAds = await _adTrackingService.GetNewAdvertisementsAsync(trackingId);

                return Ok(new
                {
                    success = true,
                    count = newAds.Count,
                    advertisements = newAds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AdTrackingController] Ошибка при получении новых объявлений: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    public class StartTrackingRequest
    {
        public string TrackingId { get; set; }
        public int CategoryId { get; set; }
        public int SubcategoryId { get; set; }
    }

    public class StopTrackingRequest
    {
        public string TrackingId { get; set; }
    }
}
