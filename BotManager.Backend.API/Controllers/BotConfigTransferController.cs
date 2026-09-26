using BotManager.Backend.API.Models;
using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BotManager.Backend.API.Controllers
{
    /// <summary>
    /// Export and import of a bot's configuration (settings, boards with teams, command customizations).
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/bot/admin")]
    public class BotConfigTransferController : ControllerBase
    {
        /// <summary>Maximum accepted import body size.</summary>
        public const int MaxImportBytes = 1024 * 1024;

        private readonly BotConfigTransferService _transfer;
        private readonly ILogger<BotConfigTransferController> _logger;

        public BotConfigTransferController(BotManagerDbContext db, ILogger<BotConfigTransferController> logger)
        {
            _transfer = new BotConfigTransferService(db);
            _logger = logger;
        }

        /// <summary>
        /// Returns the bot's configuration as a downloadable JSON document. Never includes the bot token.
        /// </summary>
        [HttpGet("{id:int}/export")]
        public async Task<IActionResult> Export(int id)
        {
            var document = await _transfer.ExportAsync(id, HttpContext.RequestAborted);
            if (document == null)
            {
                return NotFound();
            }

            Response.Headers.ContentDisposition = $"attachment; filename=\"bot-{id}-config.json\"";
            return Ok(document);
        }

        /// <summary>
        /// Imports a configuration document (mode "replace") in one transaction and returns a summary.
        /// <paramref name="dryRun"/> validates and returns the summary without writing;
        /// <paramref name="applyDiscordIds"/>=false ignores guild/channel ids from the document.
        /// </summary>
        [HttpPost("{id:int}/import")]
        [RequestSizeLimit(MaxImportBytes)]
        public async Task<IActionResult> Import(
            int id,
            [FromBody] BotConfigExportDocument? document,
            [FromQuery] string? mode = null,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool applyDiscordIds = true)
        {
            if (!string.IsNullOrEmpty(mode) && !string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Unsupported import mode.", errors = new[] { "mode: only 'replace' is supported." } });
            }

            try
            {
                var summary = await _transfer.ImportAsync(id, document, applyDiscordIds, dryRun, HttpContext.RequestAborted);
                if (!dryRun)
                {
                    _logger.LogInformation(
                        "Bot {BotId} configuration imported: boards={Boards}, teams={Teams}, commandsUpdated={Updated}, commandsCreated={Created}",
                        id, summary.BoardsImported, summary.TeamsImported, summary.CommandsUpdated, summary.CommandsCreated);
                }
                return Ok(summary);
            }
            catch (BotConfigImportValidationException ex)
            {
                return BadRequest(new { message = "Import document is invalid.", errors = ex.Errors });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}
