using BotManager.Backend.Bots.Models;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Creates, updates and re-reacts the board message of a single bot session.
    /// </summary>
    internal sealed class BoardMessagePublisher
    {
        /// <summary>
        /// Invisible content marker used to locate the board message in a channel when its id is unknown.
        /// </summary>
        internal const string BoardMessageMarker = "​";

        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBoardMessageLocator _boardMessageLocator;
        private readonly Action _onBoardMessageChanged;

        /// <summary>
        /// Creates a new board message publisher.
        /// </summary>
        /// <param name="onBoardMessageChanged">Invoked after a board message id was persisted (used to drop cached ids).</param>
        public BoardMessagePublisher(
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            IBoardMessageLocator boardMessageLocator,
            Action onBoardMessageChanged)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _boardMessageLocator = boardMessageLocator;
            _onBoardMessageChanged = onBoardMessageChanged;
        }

        /// <summary>
        /// Creates or updates the configured board message for a bot.
        /// </summary>
        public async Task<bool> RefreshBoardMessageAsync(DiscordSocketClient client, int botId, BoardMessageDto boardMessage, int? boardConfigurationId = null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var botConfig = await ResolveBoardConfigurationAsync(db, botId, boardConfigurationId);
                if (botConfig?.BoardChannelId == null)
                {
                    _logger.LogWarning("Cannot refresh board for bot {BotId}: BoardChannelId is missing", botId);
                    return false;
                }

                var boardChannel = client.GetChannel(botConfig.BoardChannelId.Value) as SocketTextChannel;
                if (boardChannel == null)
                {
                    _logger.LogWarning("Cannot refresh board for bot {BotId}: board channel {BoardChannelId} was not found", botId, botConfig.BoardChannelId.Value);
                    return false;
                }

                var embed = BoardComponentsBuilder.BuildBoardEmbed(boardMessage);
                var targetMessage = await FindBoardMessageAsync(boardChannel, botConfig);
                var components = BoardComponentsBuilder.BuildBoardComponents(boardMessage);

                if (targetMessage != null)
                {
                    await targetMessage.ModifyAsync(m =>
                    {
                        m.Content = BoardMessageMarker;
                        m.Embed = embed.Build();
                        m.Components = components.Build();
                    });

                    botConfig.BoardMessageId = targetMessage.Id;
                }
                else
                {
                    var newMessage = await boardChannel.SendMessageAsync(BoardMessageMarker, embed: embed.Build(), components: components.Build());
                    botConfig.BoardMessageId = newMessage.Id;
                }

                await db.SaveChangesAsync();
                _onBoardMessageChanged();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh board message for bot {BotId}", botId);
                return false;
            }
        }

        /// <summary>
        /// Rebuilds board message reactions when reactions already exist on the message.
        /// </summary>
        public async Task<bool> SyncBoardReactionsIfPresentAsync(DiscordSocketClient client, int botId, IEnumerable<string> emojis, int? boardConfigurationId = null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();

                var botConfig = await ResolveBoardConfigurationAsync(db, botId, boardConfigurationId);
                if (botConfig?.BoardChannelId == null)
                {
                    _logger.LogWarning("Cannot sync reactions for bot {BotId}: BoardChannelId is missing", botId);
                    return false;
                }

                var boardChannel = client.GetChannel(botConfig.BoardChannelId.Value) as SocketTextChannel;
                if (boardChannel == null)
                {
                    _logger.LogWarning("Cannot sync reactions for bot {BotId}: board channel {BoardChannelId} was not found", botId, botConfig.BoardChannelId.Value);
                    return false;
                }

                var targetMessage = await FindBoardMessageAsync(boardChannel, botConfig);
                if (targetMessage == null)
                {
                    _logger.LogInformation("Skipping reaction sync for bot {BotId}: board message was not found", botId);
                    return false;
                }

                if (targetMessage.Reactions.Count == 0)
                {
                    _logger.LogInformation("Skipping reaction sync for bot {BotId}: board message has no reactions", botId);
                    return false;
                }

                await targetMessage.RemoveAllReactionsAsync();

                var uniqueEmojis = emojis
                    .Select(e => string.IsNullOrWhiteSpace(e) ? "🎯" : e.Trim())
                    .Distinct(StringComparer.Ordinal);

                foreach (var emoji in uniqueEmojis)
                {
                    if (Emoji.TryParse(emoji, out var parsedEmoji))
                    {
                        await targetMessage.AddReactionAsync(parsedEmoji);
                    }
                }

                botConfig.BoardMessageId = targetMessage.Id;
                await db.SaveChangesAsync();
                _onBoardMessageChanged();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync board reactions for bot {BotId}", botId);
                return false;
            }
        }

        /// <summary>
        /// Loads the stored board message by id, falling back to a marker search in the channel.
        /// </summary>
        private async Task<IUserMessage?> FindBoardMessageAsync(SocketTextChannel boardChannel, BoardConfiguration botConfig)
        {
            IUserMessage? targetMessage = null;
            if (botConfig.BoardMessageId.HasValue)
            {
                targetMessage = await boardChannel.GetMessageAsync(botConfig.BoardMessageId.Value) as IUserMessage;
            }

            return targetMessage ?? await _boardMessageLocator.FindBoardMessageAsync(boardChannel, BoardMessageMarker);
        }

        /// <summary>
        /// Resolves a target board configuration for optional board scoping:
        /// the explicit configuration, else the bot's active one, else the bot's first one.
        /// </summary>
        internal static async Task<BoardConfiguration?> ResolveBoardConfigurationAsync(BotManagerDbContext db, int botId, int? boardConfigurationId)
        {
            if (boardConfigurationId.HasValue)
            {
                return await db.BoardConfigurations
                    .FirstOrDefaultAsync(c => c.BotId == botId && c.BoardConfigurationId == boardConfigurationId.Value);
            }

            var botConfiguration = await db.BotConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.BotId == botId);

            if (botConfiguration?.ActiveBoardConfigurationId != null)
            {
                var activeConfig = await db.BoardConfigurations
                    .FirstOrDefaultAsync(c => c.BotId == botId && c.BoardConfigurationId == botConfiguration.ActiveBoardConfigurationId.Value);

                if (activeConfig != null)
                {
                    return activeConfig;
                }
            }

            return await db.BoardConfigurations
                .Where(c => c.BotId == botId)
                .OrderBy(c => c.BoardConfigurationId)
                .FirstOrDefaultAsync();
        }
    }
}
