# Discord Bot Alliance - Runtime Documentation

## Overview
This document reflects the current bot runtime in this repository.

The Discord command system is implemented through plugin services, not a single monolithic `Program.cs` command block.

Primary implementation files:
- `BotManager.Backend.Bots/Services/Implementations/DiscordBotAllianceService.cs`
- `BotManager.Backend.API/BotPlugins/DiscordBoardPlugin/DiscordBoardPlugin.cs`
- `BotManager.Backend.API/BotPlugins/DiscordBoardPlugin/Services/BoardCommandService.cs`
- `BotManager.Backend.API/BotPlugins/DiscordBoardPlugin/Handlers/TeamsCommandHandler.cs`
- `BotManager.Backend.API/BotPlugins/DiscordBoardPlugin/Handlers/ReactionsCommandHandler.cs`

---

## 1. SLASH COMMANDS REGISTERED

Current registration is hierarchical and exposes one parent slash command:

| Command Name | Description | Parameters | Admin Only | Dispatcher |
|---|---|---|---|---|
| `/board` | Board management commands | None (uses subcommands) | Mixed (depends on subcommand) | `DispatchAsync` |

Registered subcommands under `/board`:

| Subcommand | Description | Parameters | Admin Only | Handler Method |
|---|---|---|---|---|
| `add-team` | Adds a new team to the roster | `name` (string, required), `leader` (string, required), `contact` (string, required) | No | `HandleAddTeamAsync` |
| `remove-team` | Removes a team from the roster | `name` (string, required) | No | `HandleRemoveTeamAsync` |
| `edit-team` | Edits an existing team | `name` (string, required), `new-name` (string, optional), `new-leader` (string, optional), `new-contact` (string, optional) | No | `HandleEditTeamAsync` |
| `refresh` | Refreshes the current team board | None | Yes | `HandleShowTeamsListAsync` |
| `insert` | Inserts/refreshes board message in configured channel | None | Yes | `HandleInsertBoardAsync` |
| `sync-reactions` | Rebuilds board message reactions from current team emojis | None | Yes | `HandleSyncBoardReactionsAsync` |

---

## 2. ACTIVE COMMAND HANDLERS

### TeamsCommandHandler
Implemented in `BotManager.Backend.API/BotPlugins/DiscordBoardPlugin/Handlers/TeamsCommandHandler.cs`.

Active handlers:
- `HandleAddTeamAsync`
- `HandleRemoveTeamAsync`
- `HandleEditTeamAsync`
- `HandleShowTeamsListAsync`
- `HandleInsertBoardAsync`

Behavior summary:
- Team CRUD operations update persisted team data through `ITeamsDataService`.
- Team changes trigger board refresh through `IDiscordBotService.RefreshBoardMessageAsync`.
- `insert` pushes current team board representation into the configured board channel/message.

### ReactionsCommandHandler
Implemented in `BotManager.Backend.API/BotPlugins/DiscordBoardPlugin/Handlers/ReactionsCommandHandler.cs`.

Active handlers:
- `HandleSyncBoardReactionsAsync`
- `HandleReactionAddedAsync`
- `HandleReactionRemovedAsync`

Behavior summary:
- `sync-reactions` removes existing board reactions and re-adds them from current team emoji values.
- Reaction add/remove events assign or remove matching Discord roles.

---

## 3. COMMAND REGISTRATION FLOW

1. Runtime starts Discord client (`DiscordBotRuntimeService`).
2. Plugin is resolved through `PluginRegistry` with plugin id `discord-board` (legacy ids are normalized).
3. `BoardCommandService.BuildCommands()` returns the `/board` command with all active subcommands.
4. Slash command execution is dispatched by subcommand name via `DispatchAsync`.

---

## 4. DATA + SERVICE DEPENDENCIES

Plugin command handlers rely on:
- `ITeamsDataService` (`BotManager.Backend.Shared.Services`)
- `IBotDataService` (`BotManager.Backend.Shared.Services`)
- `SystemConfigService`
- `IDiscordBotService`
- `CommandManagementService`

Important note:
- The runtime uses DI contracts and typed plugin context creation through `IPluginContextFactory`.

---

## 5. LEGACY NOTES

This document intentionally omits old, deprecated flat commands and old `Program.cs` line-based handler mapping.

If legacy names appear in database rows from older versions, they should be cleaned via migration/script, but they are not part of the active command catalog.
