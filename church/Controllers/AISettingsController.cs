using church.Models;
using church.AIServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace church.Controllers
{
    [ApiController]
    [Route("api/AI/settings")]
    [Authorize]
    public class AISettingsController : ControllerBase
    {
        private readonly IFirestoreSettingsService _settingsService;
        private readonly ICurrentServiceCodeResolver _serviceCodeResolver;
        private readonly context _db;

        public AISettingsController(
            IFirestoreSettingsService settingsService,
            ICurrentServiceCodeResolver serviceCodeResolver,
            context db)
        {
            _settingsService = settingsService;
            _serviceCodeResolver = serviceCodeResolver;
            _db = db;
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
        {
            var serviceCode = await ResolveCurrentServiceCodeAsync();
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
        public async Task<IActionResult> Status()
        {
            var serviceCode = await ResolveCurrentServiceCodeAsync();
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

        private async Task<string?> ResolveCurrentServiceCodeAsync()
        {
            var serviceCode = _serviceCodeResolver.Resolve(User);
            if (!string.IsNullOrWhiteSpace(serviceCode))
            {
                return serviceCode;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userId, out var userIdValue))
            {
                return null;
            }

            return await _db.Users
                .Where(user => user.Id == userIdValue)
                .Select(user => user.ChurchServices.Services.Code)
                .FirstOrDefaultAsync();
        }
    }
}
