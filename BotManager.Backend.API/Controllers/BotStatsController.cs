using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotManager.Backend.API.Controllers
{
    /// <summary>
    /// Usage statistics (daily commands/errors, top commands, uptime) for the admin dashboard and bot detail.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/bot/admin")]
    public class BotStatsController : ControllerBase
    {
        private readonly UsageStatsService _stats;
        private readonly IUserIdentityResolver _userIdentityResolver;

        public BotStatsController(BotManagerDbContext db, IUserIdentityResolver userIdentityResolver)
        {
            _stats = new UsageStatsService(db);
            _userIdentityResolver = userIdentityResolver;
        }

        /// <summary>
        /// Aggregated statistics across the current user's bots. <paramref name="days"/> is clamped to 1..90.
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats([FromQuery] int? days = null)
        {
            var ownerUserId = _userIdentityResolver.GetCurrentUserIdentifier(User);
            if (string.IsNullOrWhiteSpace(ownerUserId))
            {
                return Unauthorized("Unable to resolve current user identity.");
            }

            return Ok(await _stats.GetOwnerStatsAsync(ownerUserId, days, HttpContext.RequestAborted));
        }

        /// <summary>
        /// Statistics for a single bot. <paramref name="days"/> is clamped to 1..90.
        /// </summary>
        [HttpGet("{id:int}/stats")]
        public async Task<IActionResult> GetBotStats(int id, [FromQuery] int? days = null)
        {
            var stats = await _stats.GetBotStatsAsync(id, days, HttpContext.RequestAborted);
            return stats == null ? NotFound() : Ok(stats);
        }
    }
}
