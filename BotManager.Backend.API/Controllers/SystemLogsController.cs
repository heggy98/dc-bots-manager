using BotManager.Backend.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    /// <summary>
    /// Exposes administrative endpoints for system and login audit logs.
    /// </summary>
    public class SystemLogsController : ControllerBase
    {
        private readonly BotManagerDbContext _db;

        /// <summary>
        /// Creates a new system logs controller.
        /// </summary>
        public SystemLogsController(BotManagerDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns recent system log entries ordered by newest first.
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetSystemLogs([FromQuery] int take = 100)
        {
            var logs = await _db.SystemLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(take)
                .Select(l => new { l.Id, l.Timestamp, l.Level, l.Category, l.Message, l.Exception })
                .ToListAsync();
            return Ok(logs);
        }

        /// <summary>
        /// Returns recent login audit log entries ordered by newest first.
        /// </summary>
        [Authorize]
        [HttpGet("login-audit")]
        public async Task<IActionResult> GetLoginAuditLogs([FromQuery] int take = 100)
        {
            var logs = await _db.LoginAuditLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(take)
                .Select(l => new { l.Id, l.Timestamp, l.Email, l.IpAddress, l.Success, l.FailReason, l.IsBruteforceBlock })
                .ToListAsync();
            return Ok(logs);
        }
    }
}
