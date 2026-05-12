using Microsoft.AspNetCore.Mvc;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        private static readonly DateTime _startedAt = DateTime.UtcNow;

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
