using Microsoft.AspNetCore.Mvc;

namespace BotManager.Backend.API.Controllers
{
    /// <summary>
    /// Provides system-level health and uptime endpoints.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        private static readonly DateTime _startedAt = DateTime.UtcNow;

        /// <summary>
        /// Gets backend process start time and uptime in seconds.
        /// </summary>
        [HttpGet("uptime")]
        public IActionResult GetUptime()
        {
            var uptime = DateTime.UtcNow - _startedAt;
            return Ok(new
            {
                startedAt = _startedAt,
                uptimeSeconds = (long)uptime.TotalSeconds
            });
        }
    }
}
