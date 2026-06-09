using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services;
using BotManager.Backend.Entities.Entities;
using BotManager.Backend.Shared.Models;
using Discord;
using Discord.WebSocket;
using System.Diagnostics;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers
{
    /// <summary>
    /// Base class for board handlers that need command metadata and usage logging.
    /// </summary>
    public abstract class BoardCommandHandlerBase
    {
        private readonly IBoardCommandAuditService _boardCommandAuditService;

        protected BoardCommandHandlerBase(IBoardCommandAuditService boardCommandAuditService)
        {
            _boardCommandAuditService = boardCommandAuditService;
        }

        protected Task<BotCommand?> GetCommandAsync(string commandName, IPluginContext context)
        {
            return _boardCommandAuditService.GetCommandAsync(commandName, context);
        }

        protected Task LogCommandUsageAsync(BotCommand? command, IUser user, bool isSuccess, string? errorMessage, IPluginContext context)
        {
            return _boardCommandAuditService.LogCommandUsageAsync(command, user, isSuccess, errorMessage, context);
        }

        protected static async Task SendCommandResponseAsync(SocketSlashCommand command, string message, IPluginContext context)
        {
            var interactionAgeMs = Math.Max(0L, (long)(DateTimeOffset.UtcNow - command.CreatedAt).TotalMilliseconds);
            var attemptedLateDefer = false;

            if (interactionAgeMs >= 3000 && !command.HasResponded)
            {
                context.Logger.LogWarning(
                    "Interaction already expired before response for {CommandName}. interactionAgeMs={InteractionAgeMs}. Sending private fallback message.",
                    command.CommandName,
                    interactionAgeMs);
                await SendPrivateFallbackMessageAsync(command, message, context);
                return;
            }

            try
            {
                if (BoardInteractionResponsePolicy.ShouldAttemptLateDefer(command.HasResponded))
                {
                    attemptedLateDefer = true;
                    await TryLateDeferAsync(command, context);
                }

                if (BoardInteractionResponsePolicy.ShouldUseFollowup(command.HasResponded, interactionAgeMs))
                {
                    context.Logger.LogInformation(
                        "Sending board command response via follow-up for {CommandName}. interactionAgeMs={InteractionAgeMs} hasResponded={HasResponded} attemptedLateDefer={AttemptedLateDefer}",
                        command.CommandName,
                        interactionAgeMs,
                        command.HasResponded,
                        attemptedLateDefer);
                    await command.FollowupAsync(message, ephemeral: true);
                }
                else
                {
                    context.Logger.LogInformation(
                        "Sending board command response via initial response for {CommandName}. interactionAgeMs={InteractionAgeMs} hasResponded={HasResponded} attemptedLateDefer={AttemptedLateDefer}",
                        command.CommandName,
                        interactionAgeMs,
                        command.HasResponded,
                        attemptedLateDefer);
                    await command.RespondAsync(message, ephemeral: true);
                }
            }
            catch (Discord.Net.HttpException ex) when (BoardInteractionResponsePolicy.IsExpiredInteractionError((int?)ex.DiscordCode))
            {
                context.Logger.LogWarning(ex,
                    "Interaction webhook/token expired while sending command response for {CommandName}",
                    command.CommandName);
            }
            catch (Discord.Net.HttpException ex) when (BoardInteractionResponsePolicy.IsAlreadyAcknowledgedError((int?)ex.DiscordCode))
            {
                try
                {
                    await command.FollowupAsync(message, ephemeral: true);
                }
                catch (Discord.Net.HttpException followupEx) when (BoardInteractionResponsePolicy.IsExpiredInteractionError((int?)followupEx.DiscordCode))
                {
                    context.Logger.LogWarning(followupEx,
                        "Interaction webhook/token expired while sending follow-up command response for {CommandName}",
                        command.CommandName);
                }
            }
            catch (TimeoutException ex)
            {
                context.Logger.LogWarning(ex,
                    "Timed out while sending interaction response for {CommandName}. interactionAgeMs={InteractionAgeMs} hasResponded={HasResponded} attemptedLateDefer={AttemptedLateDefer}. The command may still have completed.",
                    command.CommandName,
                    interactionAgeMs,
                    command.HasResponded,
                    attemptedLateDefer);

                await SendPrivateFallbackMessageAsync(command, message, context);
            }
        }

        private static async Task TryLateDeferAsync(SocketSlashCommand command, IPluginContext context)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await command.DeferAsync(ephemeral: true);
                context.Logger.LogInformation(
                    "Late defer succeeded for {CommandName} in {ElapsedMs}ms.",
                    command.CommandName,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (TimeoutException ex)
            {
                context.Logger.LogWarning(ex,
                    "Late defer timed out for {CommandName} after {ElapsedMs}ms.",
                    command.CommandName,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Discord.Net.HttpException ex) when (BoardInteractionResponsePolicy.IsAlreadyAcknowledgedError((int?)ex.DiscordCode))
            {
                // The interaction was acknowledged between checks.
            }
            catch (Discord.Net.HttpException ex) when (BoardInteractionResponsePolicy.IsExpiredInteractionError((int?)ex.DiscordCode))
            {
                context.Logger.LogWarning(ex,
                    "Late defer failed because interaction expired for {CommandName}.",
                    command.CommandName);
            }
        }

        private static async Task SendPrivateFallbackMessageAsync(SocketSlashCommand command, string message, IPluginContext context)
        {
            try
            {
                await command.User.SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(ex,
                    "Failed to send private fallback response for {CommandName}",
                    command.CommandName);
            }
        }
    }
}