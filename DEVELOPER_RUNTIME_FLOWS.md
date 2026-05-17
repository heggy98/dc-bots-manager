# Developer Runtime Flows and Architecture

This document describes the current runtime behavior in this repository, based on the code as it exists now.

## Scope and Entry Point

- Main process entry point: `BotManager.Backend.API/Program.cs`.
- Main runtime path for Discord bots: `DiscordBotRuntimeService` via `IDiscordBotService`.
- Lifecycle orchestration for bot records and status: `BotManagementService`.
- API endpoints for bot lifecycle: `BotController`.
- Plugin system root: `IPluginRegistry`, `IDiscordBotPlugin`, `IPluginContextFactory`, `PluginContext`.

## Critical DI Registrations (Program.cs)

- `AddSingleton<IDiscordBotService, DiscordBotRuntimeService>()`
  - Why interface: allows lifecycle orchestrator (`BotManagementService`) to depend on an abstraction, not a concrete runtime implementation.
  - Why singleton: one in-memory Discord client/runtime instance is maintained in the API process.

- `AddSingleton<IPluginRegistry, PluginRegistry>()`
  - Why interface: plugin discovery and instantiation is abstracted from runtime dispatcher code.
  - Why singleton: plugin type registrations and active plugin cache are process-wide.

- `AddScoped<IPluginContextFactory, PluginContextFactory>()`
  - Why interface: runtime requests plugin context creation without coupling to concrete constructor details.

- `AddScoped<BotManagementService>()`
  - Used by `BotController` for start/stop/restart/data operations.

- `AddScoped<BoardCommandService>()`
- `AddScoped<IBoardCommandService>(sp => sp.GetRequiredService<BoardCommandService>())`
- `AddScoped<IDiscordCommandProvider>(sp => sp.GetRequiredService<BoardCommandService>())`
  - Why two interfaces: one interface (`IBoardCommandService`) is for plugin runtime dispatch, the other (`IDiscordCommandProvider`) is for slash command schema + default registration metadata.

- `AddScoped<TeamsCommandHandler>()`, `AddScoped<ReactionsCommandHandler>()`
  - Concrete command behavior units used by `BoardCommandService`.

- `AddScoped<ITeamsDataService, DbTeamsDataService>()`, `AddScoped<IGroupDataService, DbTeamsDataService>()`, `AddScoped<IBotDataService, DbBotDataService>()`, `AddScoped<ISystemConfigService, SystemConfigService>()`
  - Why interfaces: command/runtime logic and plugin context depend on data contracts, enabling storage implementation swaps.

- `AddScoped<CommandManagementService>()`
  - Persists command definitions and usage statistics.

## Important Non-Registration Detail

- `BotStartupService : IHostedService` exists but is not registered with `AddHostedService<BotStartupService>()`.
- Result: it is currently not launched by the host lifecycle and does not participate in startup/shutdown.

---

## 1) What Happens When the Program Starts

1. CLR enters `Program.cs` and creates bootstrap logger:
   - `Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();`

2. Inside `try`, ASP.NET host builder is created:
   - `var builder = WebApplication.CreateBuilder(args);`

3. Serilog is configured with console + SQL sink (`SystemLogs` table) using `builder.Host.UseSerilog(...)`.

4. Services are registered into DI (controllers, DbContext, auth, bot runtime, plugin services, command services, etc.).

5. JWT authentication and authorization are configured.

6. App is built:
   - `var app = builder.Build();`

7. EF migrations are applied immediately at startup:
   - `db.Database.Migrate();`

8. Middleware pipeline is configured:
   - HTTPS redirection, CORS, authentication, authorization, controller mapping.

9. Startup log is written:
   - `Log.Information("BotManager API started");`

10. Host starts request loop:
   - `app.Run();`

11. If startup throws, global catch logs fatal:
   - `Log.Fatal(ex, "Application terminated unexpectedly");`

12. `finally` always flushes logs:
   - `Log.CloseAndFlush();`

---

## 2) What Happens When a New Bot Is Added

Trigger: authenticated client calls `POST /api/bot` (`BotController.CreateBot`).

1. `BotController` is created by DI with:
   - `BotManagerDbContext`
   - `BotManagementService`
   - `DiscordBotIdentityService`
   - `ILogger<BotController>`

2. `CreateBot(CreateBotDto request)` validates required fields (`Name`, `BotToken`).

3. Current user identity is resolved from claims (`NameIdentifier`, `Email`, `sub`, etc.).

4. New `Bot` entity is created and added to `Bots` table.

5. `SaveChangesAsync()` persists the bot and generates `BotId`.

6. Default `BotConfiguration` row is created for the new bot (`BotId = newBot.BotId`).

7. `SaveChangesAsync()` persists config row.

8. Log entry is written:
   - `"New bot created: {Name} (ID {Id})"`

9. Endpoint returns `Ok(newBot.BotId)`.

Note:
- At this stage, the Discord runtime is not started yet.
- Default command metadata is not inserted yet; that happens on first bot launch in `EnsureDefaultCommandsRegisteredAsync`.

---

## 3) What Happens When a Bot Is Launched

Trigger: authenticated client calls `POST /api/bot/admin/{id}/start` (`BotController.StartBot`).

1. `BotController.StartBot(id)` calls `_botService.StartBotAsync(id)`.

2. `BotManagementService.StartBotAsync(botId)` loads bot from DB.

3. It closes any previous open run history row (`StoppedAt == null`) and writes stop reason for that stale run.

4. It inserts a new `BotRunHistory` row with current UTC start time.

5. It updates `Bot` status fields:
   - `Status = Online`
   - `LastStartedAt = UtcNow`
   - `LastStoppedAt = null`

6. It saves those DB changes.

7. It calls `_discordBotService.StartAsync(bot.BotId, bot.BotToken)`.

8. `DiscordBotRuntimeService.StartAsync`:
   - Guards against duplicate start.
   - Validates token.
   - Creates `DiscordSocketClient` with intents.
   - Subscribes handlers:
     - `Ready`
     - `Connected`
     - `Disconnected`
     - `LatencyUpdated`
     - `SlashCommandExecuted`
     - `ReactionAdded`
     - `ReactionRemoved`
   - Calls `LoginAsync` and `StartAsync`.
   - Waits up to 30s for READY (`TaskCompletionSource`).

9. On READY, `ReadyAsync()` runs:
   - Logs bot user/guild count/state.
   - Calls `RegisterCommandsViaProviderAsync()` once.

10. `RegisterCommandsViaProviderAsync` creates a scope, resolves `IDiscordCommandProvider` (`BoardCommandService`), calls `BuildCommands()`, and bulk-overwrites slash commands per guild.

11. After runtime start succeeds, `BotManagementService` calls `EnsureDefaultCommandsRegisteredAsync(botId)`:
   - pulls metadata from `IDiscordCommandProvider.GetCommandRegistrations()`
   - stores/upserts each command definition via `CommandManagementService.RegisterCommandAsync` into `BotCommands`.

12. Final log in `BotManagementService`:
   - `"Bot {BotId} ({Name}) started successfully"`

If any exception occurs:
- `BotManagementService` sets DB bot status back to Offline and logs failure.
- `DiscordBotRuntimeService` logs failure and attempts cleanup (`CleanupClientAfterFailedStartAsync`).

---

## 4) What Happens When a Bot Is Stopped

Trigger: authenticated client calls `POST /api/bot/admin/{id}/stop` (`BotController.StopBot`).

1. Controller calls `_botService.StopBotAsync(id, "Rucni vypnuti")`.

2. `BotManagementService.StopBotAsync` loads bot and open run history.

3. If open history exists, it fills:
   - `StoppedAt`
   - `DurationSeconds`
   - `StopReason`
   - `ErrorDetails` (if provided)

4. It sets bot state:
   - `Status = Offline`
   - `LastStoppedAt = UtcNow`

5. It saves DB changes.

6. It calls `_discordBotService.StopAsync()`.

7. `DiscordBotRuntimeService.StopAsync`:
   - sets `_isStopping = true`
   - cancels token source
   - calls Discord `StopAsync` + `LogoutAsync`
   - unsubscribes all client events
   - clears running flags/current bot id
   - logs stop completion

8. `BotManagementService` logs:
   - `"Bot {BotId} ({Name}) stopped. Reason: {Reason}"`

---

## 5) What Happens When a Bot Is Restarted

Trigger: authenticated client calls `POST /api/bot/admin/{id}/restart` (`BotController.RestartBot`).

1. `BotController.RestartBot(id)` calls `_botService.RestartBotAsync(id)`.

2. `BotManagementService.RestartBotAsync` executes:
   - `await StopBotAsync(botId, "Restart")`
   - `await Task.Delay(2000)`
   - `return await StartBotAsync(botId)`

3. Therefore restart is an explicit stop-then-start sequence, including all DB history/status updates and runtime reinitialization/command registration behavior from sections 3 and 4.

---

## 6) What Happens When the Program Crashes

### A) API process-level crash (host crash)

1. Unhandled exception bubbles out of startup/runtime path in `Program.cs`.

2. `catch (Exception ex)` logs:
   - `Log.Fatal(ex, "Application terminated unexpectedly")`

3. `finally` executes:
   - `Log.CloseAndFlush()`

4. Process exits.

What does not happen automatically now:
- There is no registered hosted service for graceful bot shutdown (`BotStartupService` is not wired).
- There is no explicit loop in `Program.cs` to mark every bot Offline on process crash.

### B) Discord gateway unexpected disconnect while API stays alive

1. `DiscordBotRuntimeService.DisconnectedAsync` fires (not intentional stop).

2. It logs warning/error and starts grace handling (`MarkBotOfflineAfterGracePeriodAsync`).

3. Waits 20 seconds.

4. If reconnect did not occur, it writes bot status/history to DB:
   - sets `Bot.Status = Offline`
   - sets `Bot.LastStoppedAt`
   - closes open `BotRunHistory` with reason/details

5. Logs:
   - `"Persisted unexpected disconnect to DB for bot {BotId}. Reason={Reason}"`

---

## 7) What Happens When the Program Is Shut Down

Current behavior is mostly host/process shutdown behavior from ASP.NET + Serilog.

1. Host begins shutdown (for example CTRL+C, service stop, container stop).

2. `Program.cs` top-level flow exits and reaches `finally`.

3. Serilog flushes (`Log.CloseAndFlush()`).

4. Process terminates.

Important current limitation:
- `DiscordBotRuntimeService.StopAsync()` is not guaranteed to be invoked by host shutdown because it is not an `IHostedService` and no shutdown hook calls it.
- `BotStartupService` could provide lifecycle hooks, but it is not registered in DI as hosted service.

---

## 8) What Happens When a Command Is Called

This section describes Discord slash command execution (for `/board ...`).

1. Discord emits slash interaction to connected bot.

2. `DiscordSocketClient.SlashCommandExecuted` triggers `DiscordBotRuntimeService.HandleSlashCommandExecutedAsync`.

3. Runtime validates `_currentBotId` and loads bot from DB in a new DI scope.

4. Runtime prepares plugin context via `TryPreparePluginContext(...)`:
   - `IPluginRegistry.GetOrCreatePlugin(botId, "discord-board")`
   - `IPluginContextFactory.Create(bot, db, logger, serviceProvider)`

5. If plugin has not been initialized in this runtime session, it calls `plugin.InitializeAsync(context)`.

6. Runtime calls `plugin.HandleCommandAsync(command, context)` on `DiscordBoardPlugin`.

7. `DiscordBoardPlugin.HandleCommandAsync`:
   - `command.DeferAsync(ephemeral: true)`
   - validates guild/channel/user context
   - resolves `IBoardCommandService` from context `ServiceProvider`
   - dispatches to `BoardCommandService.DispatchAsync(...)`

8. `BoardCommandService.DispatchAsync`:
   - extracts subcommand name from command options
   - looks up in in-memory command catalog
   - applies admin-permission guard when required
   - invokes mapped handler delegate

9. Handler execution:
   - Team commands: `TeamsCommandHandler` methods (`HandleAddTeamAsync`, `HandleRemoveTeamAsync`, `HandleEditTeamAsync`, `HandleShowTeamsListAsync`, `HandleInsertBoardAsync`).
   - Reaction admin commands: `ReactionsCommandHandler` methods (`HandleSyncBoardReactionsAsync`, `HandleMoveReactionMessageAsync`).

10. Handlers use services from plugin context:
   - `ITeamsDataService` for team persistence
   - `IBotDataService` for board channel/message config
   - `IDiscordBotService.RefreshBoardMessageAsync` for board updates
   - `CommandManagementService` for command usage logging (`LogCommandUsageAsync`)

11. Response/logging behavior:
   - User replies are sent as ephemeral follow-ups.
   - Success/failure and details are logged with `context.Logger` and usage logs in DB.

12. If any unhandled exception occurs in runtime dispatch path:
   - `DiscordBotRuntimeService` logs error and sends a generic user-facing failure message.

---

## Project Documentation

## 1) What Are Plugins and What Are They Used For?

A plugin is a bot behavior module implementing `IDiscordBotPlugin`.

- Contract includes:
  - lifecycle (`InitializeAsync`, `ShutdownAsync`)
  - command registration (`RegisterCommandsAsync`)
  - command handling (`HandleCommandAsync`)
  - reaction handling (`HandleReactionAddedAsync`, `HandleReactionRemovedAsync`)
  - command metadata access (`GetRegisteredCommandsAsync`)

Why plugins are used:
- isolate Discord command/event logic from host/runtime bootstrapping
- allow multiple bot behavior modules under a common runtime contract
- make per-bot plugin instantiation and context injection explicit (`PluginRegistry` + `PluginContextFactory`)
- keep command schema/provider logic reusable (`IDiscordCommandProvider`) without coupling to Discord client startup code

In this project now:
- Canonical active plugin id: `discord-board`
- Legacy aliases normalized by `PluginRegistry`: `discord-alliance`, `discord-aliance`

## 2) What Is the Principle Behind This Application and What Is Its Purpose?

Purpose:
- Manage Discord bot instances from a web API + frontend admin UI.
- Persist bot definitions, run history, config, teams/groups, and command metadata in SQL.
- Execute Discord interactions (slash commands + reactions) through a plugin-based runtime.

Design principle:
- API orchestration layer (`BotController`, `BotManagementService`) controls lifecycle and persistence.
- Runtime execution layer (`DiscordBotRuntimeService`) manages Discord gateway connectivity and event dispatch.
- Plugin behavior layer (`DiscordBoardPlugin`, handlers, command service) encapsulates domain commands.
- Data abstraction layer (`ITeamsDataService`, `IGroupDataService`, `IBotDataService`) decouples business logic from storage implementation.

Operationally, this means:
- lifecycle events update DB first, then invoke runtime actions
- runtime events can back-write status/history on unexpected disconnect
- command definitions are centrally registered and persisted, then executed through plugin-dispatch paths
- logs are written both to console and SQL (`SystemLogs`), with separate command usage logs in `CommandUsageLogs`
