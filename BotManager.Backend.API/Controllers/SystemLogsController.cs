using BotManager.Backend.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemLogsController : ControllerBase
    {
        private readonly BotManagerDbContext _db;

        public SystemLogsController(BotManagerDbContext db)
        {
            _db = db;
        }

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
