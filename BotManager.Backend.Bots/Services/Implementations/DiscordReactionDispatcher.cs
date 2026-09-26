using BotManager.Backend.Entities;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Forwards reactions on a bot's board messages to the board plugin.
    /// Keeps a short-lived cache of board message ids so reactions on unrelated messages do not hit the database.
    /// </summary>
    internal sealed class DiscordReactionDispatcher
    {
        private static readonly TimeSpan BoardMessageIdsCacheTtl = TimeSpan.FromSeconds(60);

        private readonly int _botId;
        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BoardPluginHost _pluginHost;
        private readonly Func<bool> _isSessionActive;
        private readonly Func<DiscordSocketClient?> _getClient;
        private readonly object _boardMessageIdsLock = new();
        private HashSet<ulong>? _boardMessageIds;
        private DateTime _boardMessageIdsLoadedAt;

        /// <summary>
        /// Creates a new reaction dispatcher.
        /// </summary>
        /// <param name="isSessionActive">Returns whether the owning session currently has an active bot (events are ignored otherwise).</param>
        /// <param name="getClient">Returns the session's current Discord client, used to ignore the bot's own reactions.</param>
        public DiscordReactionDispatcher(
            int botId,
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            BoardPluginHost pluginHost,
            Func<bool> isSessionActive,
            Func<DiscordSocketClient?> getClient)
        {
            _botId = botId;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _pluginHost = pluginHost;
            _isSessionActive = isSessionActive;
            _getClient = getClient;
        }

        /// <summary>
        /// Receives reaction-added events and forwards them for plugin processing.
        /// </summary>
        public Task HandleReactionAddedEventAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            Cacheable<IMessageChannel, ulong> cachedChannel,
            SocketReaction reaction)
        {
            _ = HandleReactionEventInternalAsync(
                dispatchType: ReactionDispatchType.Added,
                cachedMessage: cachedMessage,
                cachedChannel: cachedChannel,
                reaction: reaction);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Receives reaction-removed events and forwards them for plugin processing.
        /// </summary>
        public Task HandleReactionRemovedEventAsync(
            Cacheable<IUserMessage, ulong> cachedMessage,
            Cacheable<IMessageChannel, ulong> cachedChannel,
            SocketReaction reaction)
        {
            _ = HandleReactionEventInternalAsync(
                dispatchType: ReactionDispatchType.Removed,
                cachedMessage: cachedMessage,
                cachedChannel: cachedChannel,
                reaction: reaction);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Drops the cached board message ids so the next reaction reloads them.
        /// </summary>
        public void InvalidateBoardMessageIds()
        {
            lock (_boardMessageIdsLock)
            {
                _boardMessageIds = null;
            }
        }

        /// <summary>
        /// Handles reaction events by resolving bot/plugin context and invoking plugin handlers.
        /// </summary>
        private async Task HandleReactionEventInternalAsync(
            ReactionDispatchType dispatchType,
            Cacheable<IUserMessage, ulong> cachedMessage,
            Cacheable<IMessageChannel, ulong> cachedChannel,
            SocketReaction reaction)
        {
            if (!_isSessionActive())
            {
                return;
            }

            var client = _getClient();
            if (client?.CurrentUser != null && reaction.UserId == client.CurrentUser.Id)
            {
                return;
            }

            try
            {
                if (!await IsBoardMessageAsync(reaction.MessageId))
                {
                    return;
                }

                var rawChannel = await cachedChannel.GetOrDownloadAsync();
                if (rawChannel is not ISocketMessageChannel socketChannel)
                {
                    return;
                }

                if (socketChannel is not SocketGuildChannel guildChannel)
                {
                    return;
                }

                var guild = guildChannel.Guild;
                var user = guild.GetUser(reaction.UserId);
                if (user == null || user.IsBot)
                {
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var db = serviceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_botId);
                if (bot == null)
                {
                    return;
                }

                var setup = _pluginHost.TryPreparePluginContext(serviceProvider, bot, db, $"reaction:{dispatchType}");
                if (!setup.Success || setup.Plugin == null || setup.PluginContext == null)
                {
                    return;
                }

                await _pluginHost.EnsurePluginInitializedAsync(setup.Plugin, setup.PluginContext);

                if (dispatchType == ReactionDispatchType.Added)
                {
                    await setup.Plugin.HandleReactionAddedAsync(cachedMessage, socketChannel, reaction, guild, user, setup.PluginContext);
                }
                else
                {
                    await setup.Plugin.HandleReactionRemovedAsync(cachedMessage, socketChannel, reaction, guild, user, setup.PluginContext);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching reaction event {DispatchType}", dispatchType);
            }
        }

        /// <summary>
        /// Returns whether a message id is one of this bot's board messages.
        /// Uses a short-lived cache so reactions on unrelated messages do not hit the database.
        /// </summary>
        private async Task<bool> IsBoardMessageAsync(ulong messageId)
        {
            HashSet<ulong>? ids;
            lock (_boardMessageIdsLock)
            {
                ids = _boardMessageIds != null && DateTime.UtcNow - _boardMessageIdsLoadedAt < BoardMessageIdsCacheTtl
                    ? _boardMessageIds
                    : null;
            }

            if (ids == null)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();
                var loaded = await db.BoardConfigurations
                    .AsNoTracking()
                    .Where(c => c.BotId == _botId && c.BoardMessageId != null)
                    .Select(c => c.BoardMessageId!.Value)
                    .ToListAsync();

                ids = loaded.ToHashSet();
                lock (_boardMessageIdsLock)
                {
                    _boardMessageIds = ids;
                    _boardMessageIdsLoadedAt = DateTime.UtcNow;
                }
            }

            return ids.Contains(messageId);
        }

        /// <summary>
        /// Kind of reaction event being dispatched.
        /// </summary>
        private enum ReactionDispatchType
        {
            Added,
            Removed
        }
    }
}
