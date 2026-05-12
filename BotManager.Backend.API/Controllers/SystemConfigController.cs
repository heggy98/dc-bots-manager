using BotManager.Backend.API.Services;
using BotManager.Backend.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SystemConfigController : ControllerBase
    {
        private readonly SystemConfigService _configService;
        private readonly ILogger<SystemConfigController> _logger;

        public SystemConfigController(SystemConfigService configService, ILogger<SystemConfigController> logger)
        {
            _configService = configService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var configs = await _configService.GetAllAsync();
            return Ok(configs.Select(c => new { c.Id, c.Key, c.Value, c.Description }));
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] ConfigUpdateDto dto)
        {
            await _configService.SetValueAsync(dto.Key, dto.Value);
            _logger.LogInformation("System config updated: {Key} = {Value}", dto.Key, dto.Value);
            return Ok();
        }
    }

    public class ConfigUpdateDto
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
