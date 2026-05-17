using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BoardGlobalConfigController : ControllerBase
    {
        private readonly BotManagerDbContext _db;

        public BoardGlobalConfigController(BotManagerDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns board-global default rendering values.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var config = await GetOrCreateAsync();

            return Ok(new BoardGlobalConfigDto
            {
                DefaultBoardTitle = config.DefaultBoardTitle,
                DefaultBoardDescription = config.DefaultBoardDescription,
                DefaultBoardDetailSubtitleLabel = config.DefaultBoardDetailSubtitleLabel,
                DefaultBoardDetailContactLabel = config.DefaultBoardDetailContactLabel
            });
        }

        /// <summary>
        /// Updates board-global default rendering values.
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] BoardGlobalConfigDto request)
        {
            var config = await GetOrCreateAsync();

            config.DefaultBoardTitle = NormalizeNullable(request.DefaultBoardTitle);
            config.DefaultBoardDescription = NormalizeNullable(request.DefaultBoardDescription);
            config.DefaultBoardDetailSubtitleLabel = NormalizeNullable(request.DefaultBoardDetailSubtitleLabel);
            config.DefaultBoardDetailContactLabel = NormalizeNullable(request.DefaultBoardDetailContactLabel);

            await _db.SaveChangesAsync();
            return Ok();
        }

        private async Task<BoardGlobalConfig> GetOrCreateAsync()
        {
            var config = await _db.BoardGlobalConfigs.FirstOrDefaultAsync(c => c.Id == 1);
            if (config != null)
            {
                return config;
            }

            config = new BoardGlobalConfig
            {
                Id = 1,
                DefaultBoardTitle = "📋 Seznam všech skupin",
                DefaultBoardDescription = "Celkem registrovaných skupin: {count}",
                DefaultBoardDetailSubtitleLabel = "Podtitul",
                DefaultBoardDetailContactLabel = "Kontakt"
            };

            _db.BoardGlobalConfigs.Add(config);
            await _db.SaveChangesAsync();
            return config;
        }

        private static string? NormalizeNullable(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public sealed class BoardGlobalConfigDto
        {
            public string? DefaultBoardTitle { get; set; }
            public string? DefaultBoardDescription { get; set; }
            public string? DefaultBoardDetailSubtitleLabel { get; set; }
            public string? DefaultBoardDetailContactLabel { get; set; }
        }
    }
}
