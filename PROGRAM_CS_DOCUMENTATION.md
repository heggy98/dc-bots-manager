# Discord Bot Alliance - Program.cs Documentation

## Overview
The `Program.cs` file is the main entry point for the Discord bot. It handles slash command registration, command execution, emoji and role management, and all interactions with Discord servers.

**File Location:** `discord-bot-aliance/Program.cs` (1155 lines)

---

## 1. SLASH COMMANDS REGISTERED

All commands are registered in the `RegisterCommandsAsync()` method (lines 173-237).

| Command Name | Description | Parameters | Admin Only | Handler Method |
|---|---|---|---|---|
| **`/pridat-tym`** | Adds a new team to the list | `nazev` (string, required)<br/>`velitel` (string, required)<br/>`kontakt` (string, required) | No | `HandleAddTeamCommand` |
| **`/odebrat-tym`** | Removes a team from the list (including role) | `nazev` (string, required) | No | `HandleRemoveTeamCommand` |
| **`/upravit-tym`** | Edits details of an existing team | `stary-nazev` (string, required)<br/>`novy-nazev` (string, required)<br/>`novy-velitel` (string, required)<br/>`novy-kontakt` (string, required) | No | `HandleEditTeamCommand` |
| **`/reorganizeemojis`** | Reorganizes emoji for team role selection message | None | **Yes** | `HandleReorganizeEmojisCommand` |
| **`/seznam-tymu`** | Displays or updates the teams list message | None | **Yes** | `HandleShowTeamsListCommand` |
| **`/presun-reakce`** | Moves the role selection message to a new channel | `cilovy_kanal` (Channel, required) | **Yes** | `HandleMoveReactionMessageCommand` |

---

## 2. COMMAND HANDLER METHODS

### HandleAddTeamCommand (Line 668)
**Purpose:** Adds a new team to the system.

**Functionality:**
- Extracts parameters: `nazev` (team name), `velitel` (leader), `kontakt` (contact)
- Validates that the team doesn't already exist (case-insensitive)
- Creates a new `Team` object and adds it to the teams list
- Saves teams to file
- Creates a Discord role for the team with random color
- Assigns a new emoji to the team
- Updates both the teams list message and role selection message

**Data Used:**
- `Team` DTO: Name, Leader, Contact

---

### HandleRemoveTeamCommand (Line 612)
**Purpose:** Removes a team from the system.

**Functionality:**
- Extracts the team name parameter
- Validates the team exists
- Deletes the Discord role associated with the team
- Removes the team from the teams list
- Removes the emoji mapping for the team
- Updates Discord messages (teams list and role selection)
- Saves changes to files

**Data Used:**
- `Team` DTO: Name, Leader, Contact

---

### HandleEditTeamCommand (Line 706)
**Purpose:** Edits an existing team's details.

**Functionality:**
- Extracts parameters: `stary-nazev` (old name), `novy-nazev` (new name), `novy-velitel` (new leader), `novy-kontakt` (new contact)
- Validates all fields are provided
- Finds the existing team
- If team name changed:
  - Renames the Discord role
  - Updates the emoji map with new team name
- Updates team data (Name, Leader, Contact)
- Saves changes to files
- Updates Discord messages

**Data Used:**
- `Team` DTO: Name, Leader, Contact

---

### HandleReorganizeEmojisCommand (Line 778)
**Purpose:** Recreates and reorganizes the role selection message with emoji reactions.

**Functionality:**
- Admin-only check
- Finds the target channel (`seznam-týmů`)
- Deletes the old role selection message if it exists
- Creates and sends a new role selection message with current emoji mappings
- Updates `BotData` with new message ID

**Data Used:**
- `BotData` DTO: RoleMessageId
- Dictionary: teamEmojiMap

---

### HandleShowTeamsListCommand (Line 855)
**Purpose:** Creates or updates the teams list message.

**Functionality:**
- If message doesn't exist: Creates a new message with current teams list
- If message exists: Updates it with current team data
- Saves message ID to `BotData`

**Data Used:**
- `BotData` DTO: TeamsMessageId
- `Team` DTO: Name, Leader, Contact

---

### HandleMoveReactionMessageCommand (Line 359)
**Purpose:** Moves the role selection message to a different channel.

**Functionality:**
- Gets target channel from parameter
- Retrieves the old role selection message
- Collects all reactions and their users
- Creates new message in target channel with same emoji reactions
- Reapplies roles to users based on their previous reactions
- Deletes the old message
- Updates `BotData` with new message ID and channel ID

**Data Used:**
- `BotData` DTO: RoleMessageId, ReactionChannelId

---

## 3. HELPER METHODS

### TEAM MANAGEMENT

#### CreateRole (Line 1019)
**Purpose:** Creates a Discord role for a team.
- Creates role with team name, no permissions, random RGB color
- Logs the creation with color values

#### EnsureTeamsRolesExist (Line 955)
**Purpose:** Verifies that all teams have corresponding Discord roles.
- Checks each team in the list
- Prompts user (interactive console) to create missing roles
- Tracks statistics: existing, created, skipped

#### EnsureRoleSelectionMessage (Line 279)
**Purpose:** Ensures the role selection message exists.
- Checks if message ID is stored and message still exists in channel
- If not found, logs a warning

---

### EMOJI MANAGEMENT

#### AssignNewEmoji (Line 830)
**Purpose:** Assigns a unique emoji to a team.
- Finds available (unused) emoji from the emoji pool
- If all emoji are used, selects randomly from the full set
- Adds to `teamEmojiMap`
- Saves to file

#### LoadAllEmojis (Line 256)
**Purpose:** Loads all available emoji from `allEmojis.json`.
- Deserializes JSON into `EmojisWrapper` object
- Populates `allAvailableEmojis` HashSet
- Logs count of loaded emoji

#### SyncTeamEmojis (Line 1071)
**Purpose:** Synchronizes emoji map with current team list.
- **Step A:** Removes emoji mappings for deleted teams (cleanup)
- **Step B:** Identifies new teams needing emoji assignment
- **Step C:** Checks emoji availability (throws error if insufficient)
- **Step D:** Assigns emoji to new teams
- **Step E:** Saves updated map to file

---

### MESSAGE MANAGEMENT

#### SendRoleSelectionMessage (Line 303)
**Purpose:** Creates and sends the role selection embed message.
- Creates embed: "Vyber si svůj tým! ⚔️"
- Builds description from team names and emoji
- Sends message to channel
- Adds emoji reactions to the message
- Saves message ID to `BotData`

#### CreateRoleSelectionMessage (Line 329)
**Purpose:** Creates the embed and emoji mapping for role selection.
- Builds EmbedBuilder with team list and emoji
- Returns embed and teamEmojiMap dictionary

#### UpdateReactionRoleMessage (Line 580)
**Purpose:** Updates an existing role selection message.
- Fetches message from Discord
- Recreates embed with current teams/emoji
- Removes all old reactions
- Adds new reactions for each team
- Modifies the message

#### UpdateTeamsList (Line 908)
**Purpose:** Updates the teams list message with current data.
- Fetches the message by ID
- Creates new embed with team details table (Name | Leader | Contact)
- Adds current timestamp
- Modifies the message

#### CreateTeamsListDescription (Line 896)
**Purpose:** Generates formatted text for teams list.
- Creates table format: "Název týmu | Velitel | Kontakt"
- Returns formatted string with all teams

---

### REACTION HANDLERS

#### HandleReactionAddedAsync (Line 486)
**Purpose:** Assigns team role when user reacts with emoji.
- Validates reaction is on the role selection message
- Finds team name corresponding to emoji
- Removes user's previous team roles
- Assigns new team role
- Sends DM to user confirming role assignment

#### HandleReactionRemovedAsync (Line 539)
**Purpose:** Removes team role when user removes emoji reaction.
- Validates reaction is on the role selection message
- Finds team name corresponding to emoji
- Removes the role from the user
- Sends DM to user confirming role removal

---

### FILE OPERATIONS (Region: LOAD/SAVE)

#### LoadTeamsAsync (Line 1186)
**Purpose:** Loads team list from `teams.json`.
- Deserializes JSON to `List<Team>`
- Assigns emoji to any teams without emoji mapping
- Handles missing file gracefully

**File:** `teams.json`

#### SaveTeamsAsync (Line 1242)
**Purpose:** Saves team list to `teams.json`.
- Serializes `List<Team>` to formatted JSON
- Saves to file with indentation

**File:** `teams.json`

#### LoadEmojiMapAsync (Line 1271)
**Purpose:** Loads emoji-to-team mapping from `emojis.json`.
- Deserializes JSON to `Dictionary<string, string>`
- Handles missing file gracefully

**File:** `emojis.json`

#### SaveEmojiMapAsync (Line 1307)
**Purpose:** Saves emoji-to-team mapping to `emojis.json`.
- Serializes `Dictionary<string, string>` to formatted JSON
- Saves to file with indentation

**File:** `emojis.json`

#### LoadBotDataAsync (Line 433)
**Purpose:** Loads bot configuration from `botdata.json`.
- Deserializes to `BotData` object
- Uses caching (_cachedBotData) to avoid repeated disk reads
- Handles missing/corrupted file with meaningful errors

**File:** `botdata.json`

#### SaveBotDataAsync (Line 240)
**Purpose:** Saves bot configuration to `botdata.json`.
- Serializes `BotData` to formatted JSON
- Updates cache

**File:** `botdata.json`

---

### UTILITY METHODS

#### GetRandomEmoji (Line 1059)
**Purpose:** Selects a random emoji from available emoji pool.
- Uses Random to pick from `allAvailableEmojis`

#### GetLastWord (Line 1044)
**Purpose:** Extracts the last word from a log message.
- Splits by double space, returns last element trimmed

#### Log (Line 1029)
**Purpose:** Discord client logging handler.
- Filters out WebSocket and GatewayReconnect exceptions
- Logs other Discord.Net messages

#### SendAdminAlert (Line 1167)
**Purpose:** Sends critical error alerts to admin channel.
- Sends message to admin alert channel with admin mention
- Used for critical logger errors

---

## 4. DATA MODELS / DTOs

All DTOs located in `discord-bot-aliance/Dto/`

### BotData (BotData.cs)
**Purpose:** Stores Discord message and channel IDs.

```csharp
public class BotData
{
    public ulong? TeamsMessageId { get; set; }          // ID of teams list message
    public ulong? RoleMessageId { get; set; }           // ID of role selection message
    public ulong? ReactionChannelId { get; set; }       // Channel where reactions are monitored
}
```

---

### Team (Team.cs)
**Purpose:** Represents a team in the system.

```csharp
public class Team
{
    public string Name { get; set; }           // Team name
    public string Leader { get; set; }         // Team leader's name
    public string Contact { get; set; }        // Contact information (usually leader's phone)
}
```

---

### EmojisWrapper (EmojisWrapper.cs)
**Purpose:** Wraps a list of available emoji from JSON file.

```csharp
public class EmojisWrapper
{
    public List<string> emojis { get; set; }   // Array of available emoji strings
}
```

---

## 5. RELATED CLASSES & FILES

### Logger (Logger.cs)
- Custom logging class used throughout the program
- Provides `Consol()` method with various LogLevel options
- Supports critical error callback: `Logger.OnCriticalError`

### Entities (BotManager.Backend.Entities)
- `User.cs` - User login entity
- `Team.cs` - Team entity (database model)
- `Bot.cs` - Bot entity
- `BotConfiguration.cs` - Bot configuration entity
- Other supporting entities for the bot management system

### Data Files
- `teams.json` - Persistent storage of teams list
- `teams-default.json` - Default/template teams list
- `emojis.json` - Persistent storage of emoji-to-team mappings
- `emojis-default.json` - Default emoji mappings
- `botdata.json` - Bot configuration (message IDs, channel IDs)
- `allEmojis.json` - Complete list of available emoji

---

## 6. INITIALIZATION FLOW

1. **RunBotAsync()** - Main entry point
   - Configures Discord client with required intents
   - Sets up event handlers (Ready, Log, ReactionAdded, ReactionRemoved, SlashCommandExecuted)
   - Loads all emoji from `allEmojis.json`
   - Loads emoji map from `emojis.json`
   - Loads teams from `teams.json`
   - Synchronizes emoji with teams via `SyncTeamEmojis()`

2. **On Ready Event**
   - Ensures role selection message exists (`EnsureRoleSelectionMessage`)
   - Ensures all team roles exist (`EnsureTeamsRolesExist`)
   - Registers slash commands (`RegisterCommandsAsync`)

---

## 7. KEY FEATURES

- **Role-based Team Selection:** Users react to emoji to select their team role
- **Persistent Storage:** Teams, emoji mappings, and bot data saved to JSON files
- **Dynamic Emoji Assignment:** New teams automatically get assigned unique emoji
- **Admin Management:** Slash commands for adding, removing, editing teams
- **Message Management:** Ability to move role selection message to different channels
- **Error Handling:** Comprehensive logging and admin alerts for critical errors
- **Synchronized Data:** Emoji map stays in sync with team list automatically

---

## 8. CONSTANTS & CONFIGURATION

- **Channel Name:** `"seznam-týmů"` - Target channel for team role messages
- **Admin Alert Channel ID:** `1406933203851284570` - Where critical errors are logged
- **Admin User ID:** `347490212097687552` - Admin to be mentioned in alerts
- **File Paths:**
  - `teams.json`
  - `teams-default.json`
  - `emojis.json`
  - `emojis-default.json`
  - `botdata.json`
  - `allEmojis.json`

---

## 9. NOTES

⚠️ **DEPRECATED NOTICE:** The file contains a note that this bot instance is deprecated:
> "This discord-bot-aliance instance is now deprecated. Use BotManager.Api to start/manage this bot."

The bot now should be managed through the BotManager.Api system instead of running directly.
