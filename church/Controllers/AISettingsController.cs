using church.AIServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace church.Controllers
{
    [ApiController]
    [Route("api/AI/settings")]
    [Authorize]
    public class AISettingsController : ControllerBase
    {
        private readonly IFirestoreSettingsService _settingsService;
        private readonly ICurrentServiceCodeResolver _serviceCodeResolver;

        public AISettingsController(IFirestoreSettingsService settingsService, ICurrentServiceCodeResolver serviceCodeResolver)
        {
            _settingsService = settingsService;
            _serviceCodeResolver = serviceCodeResolver;
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
        {
            var serviceCode = _serviceCodeResolver.Resolve(User);
            if (string.IsNullOrWhiteSpace(serviceCode))
                return BadRequest(new { success = false, message = "Unable to resolve the current service." });

            try
            {
                var result = await _settingsService.RefreshServiceSettingsAsync(serviceCode, cancellationToken);
                return Ok(new
                {
                    success = true,
                    refreshed = true,
                    serviceCode,
                    subscriptionSettingsRefreshed = result.SubscriptionSettingsRefreshed,
                    attendanceSettingsRefreshed = result.AttendanceSettingsRefreshed,
                    refreshedAtUtc = result.RefreshedAtUtc,
                    subscriptionDocumentFound = result.SubscriptionDocumentFound,
                    attendanceDocumentFound = result.AttendanceDocumentFound
                });
            }
            catch (FirestoreSettingsService.RefreshCooldownException ex)
            {
                return Ok(new
                {
                    success = true,
                    refreshed = false,
                    reason = "cooldown",
                    serviceCode,
                    nextRefreshAllowedAtUtc = ex.NextRefreshAllowedAtUtc
                });
            }
            catch (FirestoreSettingsService.RefreshDocumentLoadException ex)
            {
                return StatusCode(503, new
                {
                    success = false,
                    serviceCode,
                    failedDocument = ex.DocumentType,
                    message = "Unable to refresh Firestore settings. Existing cached settings were preserved when available."
                });
            }
            catch (Exception)
            {
                return StatusCode(503, new
                {
                    success = false,
                    serviceCode,
                    message = "Unable to refresh Firestore settings. Existing cached settings were preserved when available."
                });
            }
        }

        [HttpGet("status")]
        public IActionResult Status()
        {
            var serviceCode = _serviceCodeResolver.Resolve(User);
            if (string.IsNullOrWhiteSpace(serviceCode))
                return BadRequest(new { success = false, message = "Unable to resolve the current service." });

            var status = _settingsService.GetCacheStatus(serviceCode);
            return Ok(new
            {
                serviceCode,
                subscription = status.Subscription,
                attendance = status.Attendance
            });
        }
    }
}
