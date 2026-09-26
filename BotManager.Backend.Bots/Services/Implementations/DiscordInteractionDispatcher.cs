using BotManager.Backend.Entities;
using BotManager.Backend.Shared.Models;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Handles Discord interactions (slash commands, board components and modals) of a single bot session:
    /// acknowledges them within Discord's response window and dispatches them to the board plugin
    /// without blocking the gateway task.
    /// </summary>
    internal sealed class DiscordInteractionDispatcher
    {
        private readonly int _botId;
        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BoardPluginHost _pluginHost;
        private readonly Func<bool> _isSessionActive;

        /// <summary>
        /// Creates a new interaction dispatcher.
        /// </summary>
        /// <param name="isSessionActive">Returns whether the owning session currently has an active bot (events are ignored otherwise).</param>
        public DiscordInteractionDispatcher(
            int botId,
            ILogger logger,
            IServiceScopeFactory scopeFactory,
            BoardPluginHost pluginHost,
            Func<bool> isSessionActive)
        {
            _botId = botId;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _pluginHost = pluginHost;
            _isSessionActive = isSessionActive;
        }

        /// <summary>
        /// Receives interactions at the earliest gateway stage, acknowledges slash commands quickly,
        /// and fire-and-forgets team-toggle button interactions.
        /// </summary>
        public async Task HandleInteractionCreatedAsync(SocketInteraction interaction)
        {
            try
            {
                if (interaction is SocketMessageComponent component)
                {
                    var customId = component.Data.CustomId;

                    // Team toggle controls (select menu, legacy button) — defer ephemerally then process.
                    if (customId.StartsWith(BoardComponentsBuilder.TeamSelectPrefix, StringComparison.Ordinal)
                        || customId.StartsWith(BoardComponentsBuilder.TeamTogglePrefix, StringComparison.Ordinal))
                    {
                        if (await EnsureComponentDeferredAsync(component, customId))
                        {
                            _ = DispatchToPluginAsync(
                                component.User,
                                "button:team_toggle",
                                "button interaction",
                                customId,
                                (plugin, guild, user, context) => plugin.HandleButtonInteractionAsync(component, guild, user, context));
                        }

                        return;
                    }

                    // "Add Team" button — the modal IS the initial response; cannot defer first.
                    if (customId == BoardComponentsBuilder.BoardAddTeamActionId)
                    {
                        await TryShowModalAsync(component, BoardComponentsBuilder.BuildAddTeamModal());
                        return;
                    }

                    // Other board action buttons (e.g. Refresh Board) — defer ephemerally then process.
                    if (customId.StartsWith(BoardComponentsBuilder.BoardActionPrefix, StringComparison.Ordinal))
                    {
                        if (await EnsureComponentDeferredAsync(component, customId))
                        {
                            _ = DispatchToPluginAsync(
                                component.User,
                                $"board_action:{customId}",
                                "board action button",
                                customId,
                                (plugin, guild, user, context) => plugin.HandleBoardActionButtonAsync(component, guild, user, context));
                        }

                        return;
                    }
                }

                // Modal submissions (e.g. the Add Team form).
                if (interaction is SocketModal modal &&
                    modal.Data.CustomId.StartsWith(BoardComponentsBuilder.BoardModalPrefix, StringComparison.Ordinal))
                {
                    if (await EnsureModalDeferredAsync(modal, modal.Data.CustomId))
                    {
                        _ = DispatchToPluginAsync(
                            modal.User,
                            $"board_modal:{modal.Data.CustomId}",
                            "board modal",
                            modal.Data.CustomId,
                            (plugin, guild, user, context) => plugin.HandleBoardModalAsync(modal, guild, user, context));
                    }

                    return;
                }

                if (interaction is not SocketSlashCommand slashCommand)
                {
                    return;
                }

                var interactionAgeAtHandlerStartMs = Math.Max(0L, (long)(DateTimeOffset.UtcNow - slashCommand.CreatedAt).TotalMilliseconds);
                var deferResult = await EnsureInteractionDeferredAsync(slashCommand);

                _logger.LogDebug(
                    "InteractionCreated defer for {CommandName} outcome={Outcome} acknowledged={Acknowledged} deferElapsedMs={DeferElapsedMs} interactionAgeAtDeferMs={InteractionAgeAtDeferMs}",
                    slashCommand.CommandName,
                    deferResult.Outcome,
                    deferResult.IsAcknowledged,
                    deferResult.AttemptElapsedMs,
                    interactionAgeAtHandlerStartMs + deferResult.AttemptElapsedMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InteractionCreated handler swallowed exception for interaction type {InteractionType}", interaction.Type);
            }
        }

        /// <summary>
        /// Handles slash command execution by preparing plugin context and dispatching the command.
        /// </summary>
        public async Task HandleSlashCommandExecutedAsync(SocketSlashCommand command)
        {
            var interactionAgeAtHandlerStartMs = Math.Max(0L, (long)(DateTimeOffset.UtcNow - command.CreatedAt).TotalMilliseconds);
            var deferResult = await EnsureInteractionDeferredAsync(command);

            _logger.LogDebug(
                "Slash command {CommandName} defer outcome={Outcome} acknowledged={Acknowledged} deferElapsedMs={DeferElapsedMs} interactionAgeAtDeferMs={InteractionAgeAtDeferMs}",
                command.CommandName,
                deferResult.Outcome,
                deferResult.IsAcknowledged,
                deferResult.AttemptElapsedMs,
                interactionAgeAtHandlerStartMs + deferResult.AttemptElapsedMs);

            // Do not block the Discord gateway task with long-running command work.
            _ = HandleSlashCommandExecutedCoreAsync(command);
        }

        /// <summary>
        /// Resolves plugin context and dispatches a component or modal interaction of a guild member to the active plugin.
        /// </summary>
        private async Task DispatchToPluginAsync(
            IUser user,
            string setupName,
            string interactionKind,
            string customId,
            Func<IDiscordBotPlugin, SocketGuild, SocketGuildUser, IPluginContext, Task> invoke)
        {
            if (!_isSessionActive())
            {
                return;
            }

            if (user is not SocketGuildUser guildUser || guildUser.IsBot)
            {
                return;
            }

            var guild = guildUser.Guild;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var db = serviceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_botId);
                if (bot == null)
                {
                    return;
                }

                var setup = _pluginHost.TryPreparePluginContext(serviceProvider, bot, db, setupName);
                if (!setup.Success || setup.Plugin == null || setup.PluginContext == null)
                {
                    return;
                }

                await _pluginHost.EnsurePluginInitializedAsync(setup.Plugin, setup.PluginContext);

                await invoke(setup.Plugin, guild, guildUser, setup.PluginContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching {InteractionKind} {CustomId}", interactionKind, customId);
            }
        }

        /// <summary>
        /// Handles slash command execution by preparing plugin context and dispatching the command.
        /// </summary>
        private async Task HandleSlashCommandExecutedCoreAsync(SocketSlashCommand command)
        {
            if (!_isSessionActive())
            {
                _logger.LogWarning("Received slash command {CommandName} but no current bot id is set", command.CommandName);
                await SendInteractionMessageAsync(command, "Bot není správně inicializován (missing bot id).");
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;
                var db = serviceProvider.GetRequiredService<BotManagerDbContext>();

                var bot = await db.Bots.FindAsync(_botId);
                if (bot == null)
                {
                    _logger.LogWarning("Received slash command {CommandName} but bot {BotId} not found", command.CommandName, _botId);
                    await SendInteractionMessageAsync(command, "Bot nenalezen v databázi.");
                    return;
                }

                var setup = _pluginHost.TryPreparePluginContext(serviceProvider, bot, db, command.CommandName);
                if (!setup.Success || setup.Plugin == null || setup.PluginContext == null)
                {
                    await SendInteractionMessageAsync(command, setup.UserMessage);
                    return;
                }

                await _pluginHost.EnsurePluginInitializedAsync(setup.Plugin, setup.PluginContext);

                var handled = await setup.Plugin.HandleCommandAsync(command, setup.PluginContext);
                if (!handled)
                {
                    _logger.LogWarning("Command {CommandName} was not handled by plugin", command.CommandName);
                    await SendInteractionMessageAsync(command, "Příkaz nebyl pluginem zpracován.");
                }
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 10015 || (int?)ex.DiscordCode == 10062)
            {
                _logger.LogWarning(ex,
                    "Interaction token expired while handling slash command {CommandName}. The command may have completed but Discord cannot accept follow-up messages.",
                    command.CommandName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while dispatching slash command {CommandName} to plugin", command.CommandName);
                await SendInteractionMessageAsync(command, "Nastala chyba při zpracování příkazu.");
            }
        }

        /// <summary>
        /// Attempts to defer a message component interaction and normalizes expected timeout/duplicate-ack failures.
        /// </summary>
        private async Task<bool> EnsureComponentDeferredAsync(SocketMessageComponent component, string source)
        {
            if (component.HasResponded)
            {
                return true;
            }

            try
            {
                await component.DeferAsync(ephemeral: true);
                return true;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timed out while deferring component interaction {Source}", source);
                return component.HasResponded;
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060 || (int?)ex.DiscordCode == 10062)
            {
                return component.HasResponded;
            }
        }

        /// <summary>
        /// Attempts to defer a modal interaction and normalizes expected timeout/duplicate-ack failures.
        /// </summary>
        private async Task<bool> EnsureModalDeferredAsync(SocketModal modal, string source)
        {
            if (modal.HasResponded)
            {
                return true;
            }

            try
            {
                await modal.DeferAsync(ephemeral: true);
                return true;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timed out while deferring modal interaction {Source}", source);
                return modal.HasResponded;
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060 || (int?)ex.DiscordCode == 10062)
            {
                return modal.HasResponded;
            }
        }

        /// <summary>
        /// Attempts to respond to a component with a modal, swallowing duplicate-ack and timeout cases.
        /// </summary>
        private async Task TryShowModalAsync(SocketMessageComponent component, Modal modal)
        {
            try
            {
                await component.RespondWithModalAsync(modal);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timed out while responding with modal for component {CustomId}", component.Data.CustomId);
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060 || (int?)ex.DiscordCode == 10062)
            {
                // Already acknowledged/expired interaction; no further response possible.
            }
        }

        /// <summary>
        /// Acknowledges slash interaction early to keep within Discord's 3-second response window.
        /// </summary>
        private async Task<InteractionDeferResult> EnsureInteractionDeferredAsync(SocketSlashCommand command)
        {
            var stopwatch = Stopwatch.StartNew();

            if (command.HasResponded)
            {
                return new InteractionDeferResult(IsAcknowledged: true, Outcome: "already-responded", AttemptElapsedMs: stopwatch.ElapsedMilliseconds);
            }

            try
            {
                await command.DeferAsync(ephemeral: true);
                return new InteractionDeferResult(IsAcknowledged: true, Outcome: "deferred", AttemptElapsedMs: stopwatch.ElapsedMilliseconds);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timed out while deferring interaction for command {CommandName}", command.CommandName);
                return new InteractionDeferResult(IsAcknowledged: command.HasResponded, Outcome: "timeout", AttemptElapsedMs: stopwatch.ElapsedMilliseconds);
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060 || (int?)ex.DiscordCode == 10062)
            {
                // Interaction was already acknowledged by another branch.
                return new InteractionDeferResult(IsAcknowledged: command.HasResponded, Outcome: $"http-{ex.DiscordCode}", AttemptElapsedMs: stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Sends an interaction response or follow-up depending on command response state.
        /// </summary>
        private static async Task SendInteractionMessageAsync(SocketSlashCommand command, string message)
        {
            try
            {
                if (command.HasResponded)
                {
                    await command.FollowupAsync(message, ephemeral: true);
                }
                else
                {
                    await command.RespondAsync(message, ephemeral: true);
                }
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 40060)
            {
                // Interaction was already acknowledged by another branch; send as follow-up instead.
                await command.FollowupAsync(message, ephemeral: true);
            }
            catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 10015 || (int?)ex.DiscordCode == 10062)
            {
                // Interaction webhook/token is no longer valid; nothing can be sent to Discord at this point.
            }
            catch (TimeoutException)
            {
                // Discord's 3-second response window was missed; command work may still have completed.
            }
        }

        /// <summary>
        /// Captures defer attempt telemetry for slash-command interactions.
        /// </summary>
        private sealed record InteractionDeferResult(bool IsAcknowledged, string Outcome, long AttemptElapsedMs);
    }
}
