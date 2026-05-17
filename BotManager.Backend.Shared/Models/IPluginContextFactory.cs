using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Shared.Models
{
    public interface IPluginContextFactory
    {
        IPluginContext Create(
            Bot bot,
            BotManagerDbContext dbContext,
            ILogger logger,
            IServiceProvider serviceProvider);
    }
}
