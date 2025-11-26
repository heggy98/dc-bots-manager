using Discord;
using Discord.WebSocket;
using discord_bot_aliance;
using discord_bot_aliance.Dto;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;
using Color = Discord.Color;
using JsonException = Newtonsoft.Json.JsonException;

class Program
{
    private DiscordSocketClient _client;
    private const string teamsFilePath = "teams.json"; // Cesta k souboru
    private const string teamsFilePathDefault = "teams-default.json"; // Cesta k souboru
    private readonly string emojiFilePath = "emojis.json";
    private readonly string emojiDefaultFilePath = "emojis-default.json";
    private readonly string dataFilePath = "botdata.json";  // Cesta k souboru
    private readonly string allEmojisFilePath = "allEmojis.json";
    private HashSet<string> allAvailableEmojis = new HashSet<string>();
    private ulong? reactionMessageId;
    private ulong? teamsMessageId = null;
    private Dictionary<string, string> teamEmojiMap = new Dictionary<string, string>();
    private List<Team> teams = new List<Team>(); // Seznam týmů
    private ILogger _logger = new Logger();
    private readonly string channelNameTabule = "seznam-týmů";
    private BotData? _cachedBotData;
    private const ulong adminAlertChannelId = 1406933203851284570;
    private const ulong adminUserId = 347490212097687552;

    static async Task Main(string[] args) => await new Program().RunBotAsync();
    public async Task RunBotAsync()
    {
        // Přidání intents
        var config = new DiscordSocketConfig
        {
            GatewayIntents =
            GatewayIntents.Guilds |
            GatewayIntents.GuildMessages |
            GatewayIntents.MessageContent |
            GatewayIntents.GuildMessageReactions |
            GatewayIntents.GuildMembers
        };

        _client = new DiscordSocketClient(config);
        _client.Ready += async () =>
        {
            var guild = _client.Guilds.FirstOrDefault();
            if (guild != null)
            {
                var botData = await LoadBotDataAsync();
                ulong? reactionChannelId = botData.ReactionChannelId;
                SocketTextChannel channel = null;
                if (reactionChannelId.HasValue)
                {
                    channel = guild.GetTextChannel(reactionChannelId.Value);
                }
                else
                {
                    channel = guild.TextChannels.FirstOrDefault(c => c.Name == channelNameTabule);
                    if (channel != null)
                    {
                        botData.ReactionChannelId = channel.Id;
                        await SaveBotDataAsync(botData);
                    }
                }

                if (channel != null)
                {
                    await EnsureRoleSelectionMessage(guild, channel);
                    await EnsureTeamsRolesExist(guild);
                }
                else
                {
                    await _logger.Consol($"(RUN BOT ASYNC) Kanál pro výběr týmu nebyl nalezen. Ujisti se, že existuje kanál '{channelNameTabule}' a že bot má přístup.", Logger.LogLevel.Error);
                }
            }
            else
            {
                await _logger.Consol("(RUN BOT ASYNC) Bot není připojen k žádnému guildu.", Logger.LogLevel.Error);
            }
            await RegisterCommandsAsync();
        };
        _client.Log += Log;
        _client.ReactionAdded += HandleReactionAddedAsync;
        _client.ReactionRemoved += HandleReactionRemovedAsync;
        _client.SlashCommandExecuted += Client_SlashCommandExecuted;
        Logger.OnCriticalError = SendAdminAlert;

        await LoadAllEmojis();
        await LoadEmojiMapAsync();
        await LoadTeamsAsync();
        await SyncTeamEmojis();

        string botToken = "MTMzMzg4NjI5ODgyMTQ5NjkyNw.GHw_VE.79Fm7vudy3voePPrcq9UxdqS7XBvaJekPkGnYI";

        await _client.LoginAsync(TokenType.Bot, botToken);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private async Task Client_SlashCommandExecuted(SocketSlashCommand command)
    {
        // Odpověz bleskurychle, aby Discord věděl, že bot příkaz přijal
        await command.DeferAsync(ephemeral: true);

        var channel = command.Channel as SocketTextChannel;
        var user = command.User as SocketGuildUser;
        var guild = user?.Guild;

        if (channel == null || guild == null)
        {
            await command.FollowupAsync("Tento příkaz lze použít pouze na serveru.", ephemeral: true);
            return;
        }

        // Spusť příslušnou logiku na základě jména příkazu
        switch (command.CommandName)
        {
            case "pridat-tym":
                await HandleAddTeamCommand(command, channel, guild);
                break;
            case "odebrat-tym":
                await HandleRemoveTeamCommand(command, channel, guild);
                break;
            case "upravit-tym":
                await HandleEditTeamCommand(command, channel, guild);
                break;
            case "reorganizeemojis":
                if (user.GuildPermissions.Administrator)
                {
                    await HandleReorganizeEmojisCommand(command, channel, guild);
                }
                else
                {
                    await command.FollowupAsync("Pro spuštění tohoto příkazu nemáš oprávnění Administrátora.", ephemeral: true);
                }
                break;
            case "seznam-tymu":
                if (user.GuildPermissions.Administrator)
                {
                    await HandleShowTeamsListCommand(command, channel);
                }
                else
                {
                    await command.FollowupAsync("Pro spuštění tohoto příkazu nemáš oprávnění Administrátora.", ephemeral: true);
                }
                break;
            case "presun-reakce":
                // Kontrola, zda má uživatel oprávnění Administrátora
                if (user.GuildPermissions.Administrator)
                {
                    await HandleMoveReactionMessageCommand(command, guild);
                }
                else
                {
                    await command.FollowupAsync("Pro spuštění tohoto příkazu nemáš oprávnění Administrátora.", ephemeral: true);
                }
                break;
        }
    }

    private async Task RegisterCommandsAsync()
    {
        var guild = _client.Guilds.FirstOrDefault(); // Měl bys mít ID svého serveru
        if (guild == null) return;

        var commands = new List<ApplicationCommandProperties>();

        // 1. /pridat-tym
        var addTeamCommand = new SlashCommandBuilder()
            .WithName("pridat-tym")
            .WithDescription("Přidá nový tým do seznamu.")
            .AddOption("nazev", ApplicationCommandOptionType.String, "Název týmu.", isRequired: true)
            .AddOption("velitel", ApplicationCommandOptionType.String, "Jméno velitele týmu.", isRequired: true)
            .AddOption("kontakt", ApplicationCommandOptionType.String, "Kontaktní informace na tým. Většinou je to mobil na velitele.", isRequired: true);
        commands.Add(addTeamCommand.Build());

        // 2. /odebrat-tym
        var removeTeamCommand = new SlashCommandBuilder()
            .WithName("odebrat-tym")
            .WithDescription("Odebere tým ze seznamu (včetně role).")
            .AddOption("nazev", ApplicationCommandOptionType.String, "Název týmu k odebrání.", isRequired: true);
        commands.Add(removeTeamCommand.Build());

        // 3. /upravit-tym
        var editTeamCommand = new SlashCommandBuilder()
            .WithName("upravit-tym")
            .WithDescription("Upraví detaily existujícího týmu.")
            .AddOption("stary-nazev", ApplicationCommandOptionType.String, "Původní název týmu.", isRequired: true)
            .AddOption("novy-nazev", ApplicationCommandOptionType.String, "Nový název týmu.", isRequired: true)
            .AddOption("novy-velitel", ApplicationCommandOptionType.String, "Nový velitel týmu.", isRequired: true)
            .AddOption("novy-kontakt", ApplicationCommandOptionType.String, "Nové kontaktní info.", isRequired: true);
        commands.Add(editTeamCommand.Build());

        var reorganizeCommand = new SlashCommandBuilder()
            .WithName("reorganizeemojis")
            .WithDescription("(Pouze ADMIN). Reorganizuje emoji u zprávy s týmy.");
        commands.Add(reorganizeCommand.Build());

        var showTeamsCommand = new SlashCommandBuilder()
            .WithName("seznam-tymu")
            .WithDescription("(Pouze ADMIN) Zobrazí nebo aktualizuje zprávu se seznamem všech zaregistrovaných týmů.");
        commands.Add(showTeamsCommand.Build());

        var moveReactionCommand = new SlashCommandBuilder()
            .WithName("presun-reakce")
            .WithDescription("(Pouze ADMIN) Přesune zprávu pro výběr rolí do nového kanálu.")
            .AddOption("cilovy_kanal", ApplicationCommandOptionType.Channel, "Kanál, kam se má zpráva přesunout.", isRequired: true);
        commands.Add(moveReactionCommand.Build());

        // Zaregistruj příkazy na server (Guild)
        try
        {
            await guild.BulkOverwriteApplicationCommandAsync(commands.ToArray());
            await _logger.Consol($"(RegisterCommandsAsync) Úspěšně registrováno {commands.Count} slash commands.");
        }
        catch (Exception ex)
        {
            await _logger.Consol($"(RegisterCommandsAsync) Chyba při registraci slash commands: {ex.Message}", Logger.LogLevel.Error);
        }
    }

    public async Task SaveBotDataAsync(BotData data)
    {
        try
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            await File.WriteAllTextAsync(dataFilePath, json);
            _cachedBotData = data;
            await _logger.Consol("(Save bot data) Konfigurace uložena a cache aktualizována.", Logger.LogLevel.Info);
        }
        catch (Exception ex)
        {
            await _logger.Consol($"(Save bot data) Chyba při ukládání: {ex.Message}", Logger.LogLevel.Error);
            throw;
        }
    }

    private async Task LoadAllEmojis()
    {
        var sb = new StringBuilder();
        sb.AppendLine("(LOAD ALL EMOJIS) Začínám načítat všechny dostupné emoji ze souboru allEmojis.json...");
        if (File.Exists(allEmojisFilePath))
        {
            var json = File.ReadAllText(allEmojisFilePath);
            var emojisWrapper = JsonConvert.DeserializeObject<EmojisWrapper>(json);
            if (emojisWrapper != null && emojisWrapper.emojis != null)
            {
                allAvailableEmojis = new HashSet<string>(emojisWrapper.emojis);
                sb.Append(" -> Všechny dostupné emoji byly načteny.");
                sb.Append($" -> Počet načtených emoji: {allAvailableEmojis.Count}");
            }
        }
        else
        {
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Error);
            await _logger.Consol("(LOAD ALL EMOJIS) Soubor allEmojis.json neexistuje!", Logger.LogLevel.Error);
        }
        await _logger.Consol(sb.ToString(), Logger.LogLevel.Info);
    }

    private async Task EnsureRoleSelectionMessage(SocketGuild guild, ITextChannel channel)
    {
        await _logger.Consol("(ROLE-EMOJIS) Kontroluju jestli existuje zpráva pro vybrání role.", Logger.LogLevel.Info);
        var botData = await LoadBotDataAsync();

        if (botData.RoleMessageId != null)
        {
            // Zkusíme najít starou zprávu
            var oldMessage = await channel.GetMessageAsync(botData.RoleMessageId.Value) as IUserMessage;
            if (oldMessage != null)
            {
                await _logger.Consol("(ROLE-EMOJIS) Zpráva pro výběr role už existuje, nepřidávám novou.", Logger.LogLevel.Info);
                reactionMessageId = botData.RoleMessageId; // Načteme ID zprávy
                return;
            }
        }

        // Pokud zpráva neexistuje
        await _logger.Consol("(ROLE-EMOJIS) Zpráva neexistuje", Logger.LogLevel.Warning);
    }

    private async Task SendRoleSelectionMessage(SocketGuild guild, ITextChannel channel)
    {
        EmbedBuilder embed;
        Dictionary<string, string> teamEmojis;
        CreateRoleSelectionMessage(out embed, out teamEmojis);

        var message = await channel.SendMessageAsync(embed: embed.Build());
        reactionMessageId = message.Id;

        // Uložení do souboru
        var botData = await LoadBotDataAsync();
        botData.RoleMessageId = reactionMessageId;
        await SaveBotDataAsync(botData);

        foreach (var emoji in teamEmojis.Values)
        {
            await message.AddReactionAsync(new Emoji(emoji));
        }

        // Uložení ID zprávy a emoji mapy
        reactionMessageId = message.Id;
        teamEmojiMap = teamEmojis;

        await _logger.Consol("(ROLE-EMOJIS) Zpráva pro výběr týmu byla odeslána!", Logger.LogLevel.Success);
    }

    private void CreateRoleSelectionMessage(out EmbedBuilder embed, out Dictionary<string, string> teamEmojis)
    {
        embed = new EmbedBuilder()
        {
            Title = "Vyber si svůj tým! ⚔️",
            Description = "Reaguj na odpovídající emoji a získáš roli svého týmu.\n\n",
            Color = Color.Green
        };

        // Use the already loaded and saved teamEmojiMap instead of creating a new one
        teamEmojis = teamEmojiMap;
        _logger.Consol($"(CREATE ROLE SELECTION MESSAGE) Počet týmů v teamEmojiMap: {teamEmojis.Count}", Logger.LogLevel.Debug);

        foreach (var team in teams)
        {
            // Check if the team exists in the emoji map before trying to use it.
            if (teamEmojis.TryGetValue(team.Name, out string emoji))
            {
                embed.Description += $"{emoji} - **{team.Name}**\n";
            }
            else
            {
                // Handle the case where a team doesn't have an emoji mapped yet.
                // This could happen if a new team is added but the bot hasn't
                // assigned an emoji and saved the map yet.
                _logger.Consol($"Emoji for team '{team.Name}' not found in map.", Logger.LogLevel.Warning);
            }
        }
    }

    private async Task HandleMoveReactionMessageCommand(SocketSlashCommand command, SocketGuild guild)
    {
        var targetChannelOption = command.Data.Options.FirstOrDefault(o => o.Name == "cilovy_kanal");
        var targetChannel = targetChannelOption?.Value as ITextChannel;
        var botData = await LoadBotDataAsync();
        var reactionMessageId = botData.RoleMessageId;
        var reactionChannelId = botData.ReactionChannelId;
        if (reactionChannelId == null || reactionMessageId == null)
        {
            await command.FollowupAsync("Chyba: ID kanálu nebo ID zprávy pro reakce nebylo nalezeno. Zpráva musí být nejprve vytvořena.", ephemeral: true);
            return;
        }
        var sourceChannel = guild.GetTextChannel(reactionChannelId.Value);
        if (sourceChannel == null)
        {
            await command.FollowupAsync($"Chyba: Kanál s ID {reactionChannelId.Value} (zdrojový kanál) nebyl na serveru nalezen. Zkontroluj, zda kanál nebyl smazán.", ephemeral: true);
            return;
        }
        var originalMessage = await sourceChannel.GetMessageAsync(reactionMessageId.Value) as IUserMessage;
        if (originalMessage == null)
        {
            await command.FollowupAsync("Původní zpráva s reakcemi nebyla nalezena. Přesun zrušen.", ephemeral: true);
            return;
        }
        var reactionsWithUsers = new Dictionary<IEmote, IEnumerable<IUser>>();
        foreach (var reaction in originalMessage.Reactions)
        {
            reactionsWithUsers.Add(reaction.Key, await originalMessage.GetReactionUsersAsync(reaction.Key, 100).FlattenAsync());
        }
        EmbedBuilder embed;
        Dictionary<string, string> teamEmojis;
        CreateRoleSelectionMessage(out embed, out teamEmojis);
        var newMessage = await targetChannel.SendMessageAsync(embed: embed.Build());
        foreach (var emoteString in teamEmojis.Values)
        {
            if (Emote.TryParse(emoteString, out var customEmote))
            {
                await newMessage.AddReactionAsync(customEmote);
            }
            else
            {
                await newMessage.AddReactionAsync(new Emoji(emoteString));
            }
        }
        foreach (var reaction in reactionsWithUsers)
        {
            // Najdi název týmu odpovídající emoji
            var teamName = teamEmojiMap.FirstOrDefault(e => e.Value == reaction.Key.Name).Key;
            if (string.IsNullOrEmpty(teamName)) continue;

            var role = guild.Roles.FirstOrDefault(r => r.Name == teamName);
            if (role == null) continue;

            foreach (var user in reaction.Value.Where(u => !u.IsBot)) // Ignorujeme bota
            {
                var guildUser = user as SocketGuildUser;
                if (guildUser != null)
                {
                    await guildUser.AddRoleAsync(role);
                    await _logger.Consol($"Přiřazena role {role.Name} uživateli {guildUser.Username} po přesunu zprávy.", Logger.LogLevel.Success);
                }
            }
        }

        botData.RoleMessageId = newMessage.Id;
        botData.ReactionChannelId = newMessage.Channel.Id;
        await SaveBotDataAsync(botData);

        // Smazání původní zprávy
        await originalMessage.DeleteAsync();

        await command.FollowupAsync($"Zpráva pro výběr rolí byla úspěšně přesunuta do kanálu: {targetChannel.Mention}. Stará zpráva byla smazána a role byly přeneseny.", ephemeral: true);
    }

    private async Task<BotData> LoadBotDataAsync()
    {
        if (_cachedBotData != null)
        {
            await _logger.Consol("(Load bot data) Data načtena z cache.", Logger.LogLevel.Debug);
            return _cachedBotData;
        }

        await _logger.Consol("(Load bot data) Hledám konfiguraci", Logger.LogLevel.Debug);

        if (!File.Exists(dataFilePath))
        {
            await _logger.Consol($"(Load bot data) Konfigurace není - neexistuje soubor: {dataFilePath}", Logger.LogLevel.Error);
            throw new FileNotFoundException("Konfigurační soubor BotData nebyl nalezen.", dataFilePath);
        }

        try
        {
            await _logger.Consol("(Load bot data) Načítám a parsuji konfiguraci.", Logger.LogLevel.Debug);
            var json = await File.ReadAllTextAsync(dataFilePath);
            var botData = JsonConvert.DeserializeObject<BotData>(json);

            if (botData == null)
            {
                await _logger.Consol("(Load bot data) Chyba: Parsování vrátilo null (soubor je pravděpodobně prázdný nebo poškozený).", Logger.LogLevel.Error);
                throw new InvalidOperationException("Chyba při parsování BotData: Deserializace vrátila null.");
            }

            _cachedBotData = botData;

            await _logger.Consol("(Load bot data) Konfigurace úspěšně načtena a uložena do cache.", Logger.LogLevel.Debug);
            await _logger.Consol($"Teams Message ID: {botData.TeamsMessageId}, Role Message ID: {botData.RoleMessageId}", Logger.LogLevel.Debug);

            return botData;
        }
        catch (JsonException ex)
        {
            await _logger.Consol($"(Load bot data) Chyba při parsování JSON souboru: {ex.Message}", Logger.LogLevel.Error);
            throw;
        }
        catch (Exception ex)
        {
            await _logger.Consol($"(Load bot data) Neočekávaná chyba při načítání: {ex.Message}", Logger.LogLevel.Error);
            throw;
        }
    }



    private async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheable1, Cacheable<IMessageChannel, ulong> cacheable2, SocketReaction reaction)
    {
        if (reaction.User.IsSpecified && !reaction.User.Value.IsBot)
        {
            var areReactionsSame = reaction.MessageId == reactionMessageId;
            if (reactionMessageId != null && areReactionsSame)
            {
                var teamName = teamEmojiMap.FirstOrDefault(e => e.Value == reaction.Emote.Name).Key;
                await _logger.Consol($"(ROLE-EMOJIS-REACTION-ADDED) Uživatel {reaction.User.Value.Username} reagoval na zprávu. Chce si vybrat tým {teamName}.", Logger.LogLevel.Debug);
                var guild = (reaction.Channel as SocketGuildChannel)?.Guild;
                if (guild == null) return;
                var user = guild.GetUser(reaction.UserId);
                if (user == null) return;
                var anythingChanged = false;
                foreach (var entry in teamEmojiMap)
                {
                    if (reaction.Emote.Name == entry.Value)
                    {
                        var newRole = guild.Roles.FirstOrDefault(r => r.Name == entry.Key);
                        if (newRole == null) continue;

                        // Najdeme všechny týmové role, které má uživatel (může být jen jedna, ale jistota je jistota)
                        // Najdi všechny role, které má uživatel a jsou z tvých týmů
                        var existingTeamRoles = user.Roles.Where(r => teamEmojiMap.ContainsKey(r.Name) && r.Id != newRole.Id).ToList();

                        // Pokud existují, odeber je
                        if (existingTeamRoles.Any())
                        {
                            await user.RemoveRolesAsync(existingTeamRoles);
                            await _logger.Consol($"Uživatel {user.Username} byl odebrán z rolí: {string.Join(", ", existingTeamRoles.Select(r => r.Name))}.", Logger.LogLevel.Success);
                            anythingChanged = true;
                        }

                        // Přidání nové role, pokud ji ještě nemá
                        if (!user.Roles.Contains(newRole))
                        {
                            await user.AddRoleAsync(newRole);
                            await user.SendMessageAsync($"Byla ti přiřazena role **{newRole.Name}**!");
                            await _logger.Consol($"Uživatel {user.Username} si vybral tým {newRole.Name}.", Logger.LogLevel.Success);
                            anythingChanged = true;
                        }
                        break;
                    }
                }
                if (anythingChanged)
                {
                    await _logger.Consol($"(ROLE-EMOJIS-REACTION-ADDED) Uživatel {user.Username} změnil své týmové role.", Logger.LogLevel.Info);
                }
            }
            await _logger.Consol($"(ROLE-EMOJIS-REACTION-ADDED) Konec zpracování reakce uživatele {reaction.User.Value.Username}.", Logger.LogLevel.Debug);
        }
    }

    private async Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> cacheable1, Cacheable<IMessageChannel, ulong> cacheable2, SocketReaction reaction)
    {
        if (reaction.User.IsSpecified && !reaction.User.Value.IsBot)
        {
            var teamName = teamEmojiMap.FirstOrDefault(e => e.Value == reaction.Emote.Name).Key;
            await _logger.Consol($"(ROLE-EMOJIS-REACTION-REMOVED) Uživatel {reaction.User.Value.Username} reagoval na zprávu. Chce si odebrat tým {teamName}.", Logger.LogLevel.Debug);
            var guild = (reaction.Channel as SocketGuildChannel)?.Guild;
            if (guild == null) return;

            var user = guild.GetUser(reaction.UserId);
            if (user == null) return;

            var areReactionsSame = reaction.MessageId == reactionMessageId;
            await _logger.Consol($"(ROLE-EMOJIS-REACTION-REMOVED) Porovnávám reakci s ID zprávy: {reactionMessageId} | {reaction.MessageId} -> jsou stejné?: {areReactionsSame}.", Logger.LogLevel.Debug);

            if (reactionMessageId != null && reactionMessageId == reaction.MessageId)
            {
                var anythingChanged = false;
                foreach (var entry in teamEmojiMap)
                {
                    if (reaction.Emote.Name == entry.Value)
                    {
                        var role = guild.Roles.FirstOrDefault(r => r.Name == entry.Key);
                        if (role != null && user.Roles.Contains(role))
                        {
                            await user.RemoveRoleAsync(role);
                            await user.SendMessageAsync($"Byla ti odebrána role **{role.Name}**.");
                            await _logger.Consol($"(ROLE-EMOJIS-REACTION-REMOVED) Uživatel {user.Username} si odebral tým {role.Name}.", Logger.LogLevel.Success);
                            anythingChanged = true;
                        }
                    }
                }
                if (anythingChanged)
                {
                    await _logger.Consol($"(ROLE-EMOJIS-REACTION-REMOVED) Uživatel {user.Username} změnil své týmové role.", Logger.LogLevel.Info);
                }
            }
            await _logger.Consol($"(ROLE-EMOJIS-REACTION-REMOVED) Konec zpracování reakce uživatele {reaction.User.Value.Username}.", Logger.LogLevel.Debug);
        }
    }

    private async Task UpdateReactionRoleMessage(SocketTextChannel channel)
    {
        await _logger.Consol("(ROLE-EMOJIS-UPDATE MESSAGE)", Logger.LogLevel.Debug);
        var message = await channel.GetMessageAsync(reactionMessageId.Value) as IUserMessage;
        if (message != null)
        {
            await _logger.Consol("(ROLE-EMOJIS-UPDATE MESSAGE) Message is not null", Logger.LogLevel.Debug);
            EmbedBuilder embed;
            var teamEmojis = new Dictionary<string, string>();
            CreateRoleSelectionMessage(out embed, out teamEmojis);
            await _logger.Consol("(ROLE-EMOJIS-UPDATE MESSAGE) Created embed message", Logger.LogLevel.Debug);
            await message.ModifyAsync(m => m.Embed = embed.Build());
            await _logger.Consol("(ROLE-EMOJIS-UPDATE MESSAGE) Modifying async...", Logger.LogLevel.Debug);
            var existingReactions = message.Reactions.Select(kv => kv.Key.Name).ToList();
            foreach (var reaction in existingReactions)
            {
                await _logger.Consol("(ROLE-EMOJIS-UPDATE MESSAGE) Removing reaction " + reaction.ToString(), Logger.LogLevel.Info);
                await message.RemoveAllReactionsForEmoteAsync(new Emoji(reaction));
            }

            foreach (var team in teamEmojiMap)
            {
                await _logger.Consol("(ROLE-EMOJIS-UPDATE MESSAGE) Adding reaction for team " + team.Key + " with emoji " + team.Value.ToString(), Logger.LogLevel.Info);
                await message.AddReactionAsync(new Emoji(team.Value));
            }

            await _logger.Consol("(ROLE-EMOJIS-UPDATE MESSAGE) Zpráva s emoji byla aktualizována.", Logger.LogLevel.Success);
        }
    }



    private async Task HandleRemoveTeamCommand(SocketSlashCommand command, SocketTextChannel channel, SocketGuild guild)
    {
        await _logger.Consol("(REMOVE TEAM) Začínám proces odebrání týmu...", Logger.LogLevel.Info);

        // Získáme název týmu z povinného parametru "nazev"
        string teamName = command.Data.Options.FirstOrDefault(o => o.Name == "nazev")?.Value.ToString();

        // Kontrola, zda je parametr platný
        if (string.IsNullOrWhiteSpace(teamName))
        {
            await command.FollowupAsync("Chyba: Prosím, zadej název týmu, který chceš odebrat.", ephemeral: true);
            await _logger.Consol("(REMOVE TEAM) Chyba: Název týmu není zadán nebo je prázdný.", Logger.LogLevel.Warning);
            return;
        }

        // Najdi existující tým (case-insensitive)
        var team = teams.FirstOrDefault(t => t.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase));

        if (team != null)
        {
            // 2. Odebrání role na Discordu
            var role = guild.Roles.FirstOrDefault(r => r.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase));
            if (role != null)
            {
                await role.DeleteAsync();
                await _logger.Consol($"Role '{team.Name}' byla smazána.", Logger.LogLevel.Info);
            }

            // 3. Odebrání z týmového seznamu a uložení
            teams.Remove(team);
            await SaveTeamsAsync();

            // 4. Odebrání z emoji mapy a uložení
            if (teamEmojiMap.ContainsKey(team.Name))
            {
                teamEmojiMap.Remove(team.Name);
                await SaveEmojiMapAsync();
            }

            // 5. Aktualizace Discord zpráv
            await UpdateTeamsList(channel);
            await UpdateReactionRoleMessage(channel);

            // Oznámení pro uživatele (viditelné pouze jim)
            await command.FollowupAsync($"Tým **{team.Name}** byl úspěšně odebrán.", ephemeral: true);
            await _logger.Consol($"(REMOVE TEAM) Tým '{teamName}' byl úspěšně odebrán.", Logger.LogLevel.Success);
        }
        else
        {
            // Chyba: Tým nenalezen
            await command.FollowupAsync($"Tým **{teamName}** se v seznamu nenachází.", ephemeral: true);
        }

        await _logger.Consol($"(REMOVE TEAM) Proces odebrání týmu '{teamName}' dokončen.", Logger.LogLevel.Info);
    }

    private async Task HandleAddTeamCommand(SocketSlashCommand command, SocketTextChannel channel, SocketGuild guild)
    {
        await _logger.Consol("(ADD TEAM) Začínám proces přidání týmu...", Logger.LogLevel.Info);
        string teamName = command.Data.Options.FirstOrDefault(o => o.Name == "nazev")?.Value.ToString();
        string leaderName = command.Data.Options.FirstOrDefault(o => o.Name == "velitel")?.Value.ToString();
        string contact = command.Data.Options.FirstOrDefault(o => o.Name == "kontakt")?.Value.ToString() ?? "N/A";

        if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(leaderName))
        {
            // I když jsou parametry povinné, tato kontrola zajišťuje, že nejsou prázdné/null
            await command.FollowupAsync("Chyba: Název týmu a jméno velitele jsou povinné a nemohou být prázdné.", ephemeral: true);
            await _logger.Consol("(ADD TEAM) Chyba: Povinné parametry nejsou vyplněny.", Logger.LogLevel.Warning);
            return;
        }

        if (teams.Any(t => t.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase)))
        {
            await command.FollowupAsync($"Tým **{teamName}** už je v seznamu!", ephemeral: true);
            await _logger.Consol($"(ADD TEAM) Chyba: Tým '{teamName}' již existuje.", Logger.LogLevel.Warning);
            return;
        }

        // Tvá stávající logika pro přidání týmu...
        var team = new Team { Name = teamName, Leader = leaderName, Contact = contact };
        teams.Add(team);
        await SaveTeamsAsync();

        await CreateRole(guild, team);
        await AssignNewEmoji(teamName);

        await UpdateTeamsList(channel);
        await UpdateReactionRoleMessage(channel);

        await command.FollowupAsync($"Tým **{teamName}** byl přidán do seznamu!", ephemeral: true);
        await _logger.Consol($"(ADD TEAM) Tým '{teamName}' byl úspěšně přidán.", Logger.LogLevel.Success);
        await _logger.Consol("(ADD TEAM) Proces přidání týmu dokončen.", Logger.LogLevel.Info);
    }

    private async Task HandleEditTeamCommand(SocketSlashCommand command, SocketTextChannel channel, SocketGuild guild)
    {
        await _logger.Consol("(EDIT TEAM) Začínám proces úpravy týmu...", Logger.LogLevel.Info);
        // 1. Získání parametrů z lomeného příkazu
        // Předpokládáme, že Slash Command byl registrován s těmito názvy:
        // "stary-nazev", "novy-nazev", "novy-velitel", "novy-kontakt"

        // Získání hodnot (jsou povinné, ale pro jistotu kontrolujeme null/prázdný string)
        string oldName = command.Data.Options.FirstOrDefault(o => o.Name == "stary-nazev")?.Value.ToString();
        string newName = command.Data.Options.FirstOrDefault(o => o.Name == "novy-nazev")?.Value.ToString();
        string newLeader = command.Data.Options.FirstOrDefault(o => o.Name == "novy-velitel")?.Value.ToString();
        string newContact = command.Data.Options.FirstOrDefault(o => o.Name == "novy-kontakt")?.Value.ToString();

        // Kontrola, zda jsou klíčové parametry zadány
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || string.IsNullOrWhiteSpace(newLeader) || string.IsNullOrWhiteSpace(newContact))
        {
            await command.FollowupAsync("Chyba: Všechna pole (starý název, nový název, velitel, kontakt) musí být vyplněna.", ephemeral: true);
            await _logger.Consol("(EDIT TEAM) Chyba: Nejsou vyplněna všechna pole.", Logger.LogLevel.Warning);
            return;
        }

        // Najdi existující tým
        var teamToEdit = teams.FirstOrDefault(t => t.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));

        if (teamToEdit == null)
        {
            await command.FollowupAsync($"Tým s názvem **{oldName}** nebyl nalezen.", ephemeral: true);
            await _logger.Consol($"(EDIT TEAM) Tým '{oldName}' nebyl nalezen pro úpravu.", Logger.LogLevel.Warning);
            return;
        }

        // Zkontroluj, zda se změnilo jméno (důležité pro aktualizaci role a emoji mapy)
        bool nameChanged = !teamToEdit.Name.Equals(newName, StringComparison.OrdinalIgnoreCase);

        // 1. Změna jména, role a emoji mapy
        if (nameChanged)
        {
            // 1a. Aktualizuj roli na Discordu
            var oldRole = guild.Roles.FirstOrDefault(r => r.Name.Equals(teamToEdit.Name, StringComparison.OrdinalIgnoreCase));
            if (oldRole != null)
            {
                await oldRole.ModifyAsync(r => r.Name = newName);
                await _logger.Consol($"(EDIT TEAM) Role '{oldName}' byla přejmenována na '{newName}'.");
            }

            // 1b. Aktualizuj emoji mapu
            if (teamEmojiMap.ContainsKey(teamToEdit.Name))
            {
                var emoji = teamEmojiMap[teamToEdit.Name];
                teamEmojiMap.Remove(teamToEdit.Name);
                teamEmojiMap[newName] = emoji;
                await SaveEmojiMapAsync();
                await _logger.Consol($"(EDIT TEAM) Emoji mapa byla aktualizována, klíč '{oldName}' nahrazen klíčem '{newName}'.");
            }
        }

        // 2. Aktualizuj data týmu v seznamu
        teamToEdit.Name = newName;
        teamToEdit.Leader = newLeader;
        teamToEdit.Contact = newContact;

        // 3. Ulož a aktualizuj zprávy
        await SaveTeamsAsync();
        await UpdateTeamsList(channel);          // Aktualizuje seznam týmů
        await UpdateReactionRoleMessage(channel); // Aktualizuje zprávu pro výběr rolí

        // Odešle potvrzující zprávu (viditelnou pouze uživateli)
        await command.FollowupAsync($"Tým **{oldName}** byl úspěšně upraven na **{newName}** (Velitel: {newLeader}, Kontakt: {newContact}).", ephemeral: true);
        await _logger.Consol($"(EDIT TEAM) Tým '{oldName}' byl úspěšně upraven na '{newName}'.", Logger.LogLevel.Success);
        await _logger.Consol("(EDIT TEAM) Proces úpravy týmu dokončen.", Logger.LogLevel.Info);
    }

    private async Task HandleReorganizeEmojisCommand(SocketSlashCommand command, SocketTextChannel adminChannel, SocketGuild guild)
    {
        await _logger.Consol("(REORGANIZE EMOJIS) Spouštím reorganizaci emoji pro výběr rolí...", Logger.LogLevel.Info);
        // Kontrola, zda máme instanci cechu/serveru (guild)
        if (guild == null)
        {
            await command.FollowupAsync("Chyba: Server nebyl nalezen. Napiš Heggymu co to zase blbne.", ephemeral: true);
            await _logger.Consol("(REORGANIZE EMOJIS) Chyba: Guild (server) je null.", Logger.LogLevel.Error);
            return;
        }

        // Najdi cílový kanál, kam se mají emoji zprávy posílat
        // Použijeme tvou globální proměnnou channelNameTabule
        var targetChannel = guild.TextChannels.FirstOrDefault(c => c.Name == channelNameTabule);

        if (targetChannel == null)
        {
            await command.FollowupAsync($"Chyba: Cílový kanál '{channelNameTabule}' nebyl nalezen. Napiš Heggymu co to zase blbne.", ephemeral: true);
            await _logger.Consol($"(REORGANIZE EMOJIS) Chyba: Cílový kanál '{channelNameTabule}' nebyl nalezen na serveru.", Logger.LogLevel.Error);
            return;
        }

        var botData = await LoadBotDataAsync();

        // Zkus najít a smazat starou zprávu v cílovém kanálu
        if (botData.RoleMessageId.HasValue)
        {
            try
            {
                var oldMessage = await targetChannel.GetMessageAsync(botData.RoleMessageId.Value);
                if (oldMessage != null)
                {
                    await oldMessage.DeleteAsync();
                    await _logger.Consol("(REORGANIZE EMOJIS) Stará zpráva s výběrem rolí byla smazána.", Logger.LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                await _logger.Consol($"(REORGANIZE EMOJIS) Varování: Nepodařilo se smazat starou zprávu s ID {botData.RoleMessageId.Value}: {ex.Message}", Logger.LogLevel.Warning);
                // Pokračujeme dál, i když se smazání nepodařilo (např. zpráva už neexistuje)
            }
        }

        // Vytvoří a odešle novou zprávu do cílového kanálu
        await SendRoleSelectionMessage(guild, targetChannel);

        // Pošli potvrzení administrátorovi (viditelné jen jemu)
        await command.FollowupAsync("Zpráva s emoji pro výběr rolí byla úspěšně reorganizována v kanálu: " + targetChannel.Mention, ephemeral: true);
        await _logger.Consol("(REORGANIZE EMOJIS) Reorganizace emoji dokončena.", Logger.LogLevel.Success);
        await _logger.Consol("(REORGANIZE EMOJIS) Proces reorganizace emoji dokončen.", Logger.LogLevel.Info);
    }

    private async Task AssignNewEmoji(string teamName)
    {
        await _logger.Consol($"(ASSIGN NEW EMOJI) Přiřazuji novou emoji týmu {teamName}", Logger.LogLevel.Info);
        // Najdi emoji, které ještě nebyly použity
        var usedEmojis = new HashSet<string>(teamEmojiMap.Values);
        var availableForUse = allAvailableEmojis.Except(usedEmojis).ToList();

        string emoji;
        if (availableForUse.Any())
        {
            var random = new Random();
            emoji = availableForUse[random.Next(availableForUse.Count)];
        }
        else
        {
            // Pokud jsou všechna emoji použita, vyber náhodně z celé sady
            await _logger.Consol("(ASSIGN NEW EMOJI) Všechna emoji byla použita, vyberu náhodně z celé sady.", Logger.LogLevel.Warning);
            emoji = GetRandomEmoji();
        }

        teamEmojiMap[teamName] = emoji;
        await SaveEmojiMapAsync();
        await _logger.Consol($"(ASSIGN NEW EMOJI) Emoji '{emoji}' byla přiřazena týmu '{teamName}'", Logger.LogLevel.Success);
    }

    private async Task HandleShowTeamsListCommand(SocketSlashCommand command, ITextChannel channel)
    {
        await _logger.Consol("(SHOW TEAMS LIST) Zpracovávám příkaz pro zobrazení/aktualizaci seznamu týmů...", Logger.LogLevel.Info);
        if (teamsMessageId == null)
        {
            // 1. Zpráva neexistuje -> Vytvořit novou (použijeme logiku z původního kódu)

            var embed = new EmbedBuilder()
            {
                Title = "Seznam týmů",
                Color = Color.Blue
            };

            // Zde voláme metodu pro vytvoření textového popisu seznamu týmů
            string teamsList = CreateTeamsListDescription();

            embed.Description = teamsList;

            // Poslání nové zprávy a uložení jejího ID
            var message = await channel.SendMessageAsync(embed: embed.Build());
            teamsMessageId = message.Id;

            // Uložíme ID zprávy do souboru
            var botData = await LoadBotDataAsync();
            botData.TeamsMessageId = teamsMessageId;
            await SaveBotDataAsync(botData);

            await command.FollowupAsync("Zpráva se seznamem týmů byla vytvořena. Její ID bylo uloženo.", ephemeral: true);
            await _logger.Consol("(SHOW TEAMS LIST) Zpráva se seznamem týmů byla vytvořena a uložena.", Logger.LogLevel.Success);
        }
        else
        {
            await _logger.Consol("(SHOW TEAMS LIST) Zpráva se seznamem týmů už existuje, aktualizuji ji...", Logger.LogLevel.Debug);
            // 2. Zpráva už existuje -> Jen ji aktualizuj
            await UpdateTeamsList(channel);

            await command.FollowupAsync("Zpráva se seznamem týmů byla aktualizována.", ephemeral: true);
            await _logger.Consol("(SHOW TEAMS LIST) Zpráva se seznamem týmů byla aktualizována.", Logger.LogLevel.Success);
        }
    }

    private string CreateTeamsListDescription()
    {
        string teamsList = "Název týmu | Velitel | Kontakt\n" +
                            "-------------------------\n";

        foreach (var team in teams)
        {
            teamsList += $"{team.Name} | {team.Leader} | {team.Contact}\n";
        }
        return teamsList;
    }

    private async Task UpdateTeamsList(ITextChannel channel)
    {
        await _logger.Consol("(UPDATE TEAMS LIST) Aktualizuji zprávu se seznamem týmů...", Logger.LogLevel.Info);
        if (teamsMessageId != null)
        {
            // Získání zprávy jako IUserMessage, což je typ, který podporuje ModifyAsync
            var message = await channel.GetMessageAsync(teamsMessageId.Value) as IUserMessage;

            if (message != null)
            {
                var embed = new EmbedBuilder()
                {
                    Title = "Seznam týmů",
                    Color = Color.Blue
                };

                string teamsList = "Název týmu | Velitel | Kontakt\n" +
                                   "-------------------------\n";

                foreach (var team in teams)
                {
                    teamsList += $"{team.Name} | {team.Leader} | {team.Contact}\n";
                }

                await _logger.Consol($"(UPDATE TEAMS LIST) Počet týmů: {teams.Count}", Logger.LogLevel.Debug);

                embed.Description = teamsList;

                // Upravení existující zprávy
                await message.ModifyAsync(msg => msg.Embed = embed.Build());

                // Uložení ID zprávy po každé změně
                var botData = await LoadBotDataAsync();
                botData.TeamsMessageId = teamsMessageId;
                await SaveBotDataAsync(botData);
                await _logger.Consol("(UPDATE TEAMS LIST) Zpráva se seznamem týmů byla aktualizována.", Logger.LogLevel.Success);
            }
        }
    }

    private async Task EnsureTeamsRolesExist(SocketGuild guild)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(ENSURE ROLES) Spouštím kontrolu existence týmových rolí...");

        // 1. Bezpečnostní kontrola
        if (teams == null || teams.Count == 0)
        {
            await _logger.Consol("(ENSURE ROLES) Seznam týmů je prázdný, není co kontrolovat.", Logger.LogLevel.Warning);
            return;
        }

        // Statistiky pro závěrečný log
        int alreadyExistsCount = 0;
        int createdCount = 0;
        int skippedCount = 0;

        // Pokud je týmů hodně, vytáhneme si názvy existujících rolí do HashSetu pro rychlost
        var existingRoleNames = new HashSet<string>(guild.Roles.Select(r => r.Name));

        try
        {
            foreach (var team in teams)
            {
                if (existingRoleNames.Contains(team.Name))
                {
                    alreadyExistsCount++;
                }
                else
                {
                    await _logger.Consol($"(ENSURE ROLES) Role '{team.Name}' neexistuje. Vytvořit? [A]no / [N]e", Logger.LogLevel.Warning);
                    var input = Console.ReadKey(intercept: true); // 'intercept: true' nevypíše znak do konzole
                    Console.WriteLine(); // Odřádkování po stisku klávesy

                    if (input.Key == ConsoleKey.A)
                    {
                        await CreateRole(guild, team);
                        sb.AppendLine($" -> Vytvořena role: {team.Name}");
                        createdCount++;

                        // Přidáme do HashSetu, kdyby se v seznamu tým opakoval (pojistka)
                        existingRoleNames.Add(team.Name);
                    }
                    else
                    {
                        sb.AppendLine($" -> Přeskočeno: {team.Name} (uživatel zamítl)");
                        skippedCount++;
                    }
                }
            }

            sb.Append($" -> Kontrola dokončena. Existovalo: {alreadyExistsCount}, Vytvořeno: {createdCount}, Přeskočeno (zamítnuto): {skippedCount}.");

            // Barva logu podle toho, jestli se něco dělo
            var finalLogLevel = (createdCount > 0 || skippedCount > 0) ? Logger.LogLevel.Success : Logger.LogLevel.Info;

            await _logger.Consol(sb.ToString(), finalLogLevel);
        }
        catch (Exception ex)
        {
            await _logger.Consol($"(ENSURE ROLES) Chyba při kontrole rolí: {ex.Message}", Logger.LogLevel.Error);
        }
    }

    private async Task CreateRole(SocketGuild guild, Team team)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(CREATE ROLE) Vytvářím roli pro tým " + team.Name);
        var randomizer = new Random();
        var randomColor = new Color(randomizer.Next(256), randomizer.Next(256), randomizer.Next(256));
        var newRole = await guild.CreateRoleAsync(team.Name, GuildPermissions.None, randomColor, true, true);
        sb.Append($" -> Role '{team.Name}' byla vytvořena s barvou RGB({randomColor.R}, {randomColor.G}, {randomColor.B}).");
        await _logger.Consol(sb.ToString(), Logger.LogLevel.Info);
    }

    private async Task Log(LogMessage msg)
    {
        string messageContent = msg.ToString();
        if (msg.Severity >= LogSeverity.Error && (messageContent.Contains("WebSocketException")) || messageContent.Contains("GatewayReconnectException"))
        {
            if(messageContent.Contains("GatewayReconnectException"))
            {
                await _logger.Consol("DISCORD SERVER LOG: Server requested a reconnect", Logger.LogLevel.DiscordClientLog);
            }
            return;
        }

        await _logger.Consol("DISCORD SERVER LOG: " + messageContent, Logger.LogLevel.DiscordClientLog);
        await Task.CompletedTask;
    }

    private string GetRandomEmoji()
    {
        var random = new Random();
        return allAvailableEmojis.ElementAt(random.Next(allAvailableEmojis.Count));
    }

    private async Task SyncTeamEmojis()
    {
        var sb = new StringBuilder();
        sb.AppendLine("(SYNC EMOJIS) Spouštím synchronizaci emoji s týmy...");

        // 1. Bezpečnostní kontrola - pokud nemáme týmy nebo emoji, nemá smysl pokračovat
        if (teams == null || allAvailableEmojis == null)
        {
            sb.Append(" -> Nelze synchronizovat: Seznam týmů nebo dostupných emoji je null.");
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Warning);
            return;
        }

        // 2. Snapshot týmů (pro rychlé vyhledávání a thread-safety)
        // HashSet je super rychlý pro zjišťování "obsahuje/neobsahuje"
        var currentTeamNames = new HashSet<string>(teams.Select(t => t.Name));

        // ---------------------------------------------------------
        // KROK A: ODSTRANĚNÍ NEPLATNÝCH (Cleanup)
        // ---------------------------------------------------------
        // Najdeme klíče v mapě, které už nejsou v aktuálním seznamu týmů
        var teamsToRemove = teamEmojiMap.Keys.Where(k => !currentTeamNames.Contains(k)).ToList();

        foreach (var oldTeam in teamsToRemove)
        {
            if (teamEmojiMap.TryGetValue(oldTeam, out string freedEmoji))
            {
                sb.AppendLine($" -> Odebráno: {oldTeam} (uvolněn emoji {freedEmoji})");
                teamEmojiMap.Remove(oldTeam);
            }
        }

        // ---------------------------------------------------------
        // KROK B: PŘÍPRAVA NOVÝCH
        // ---------------------------------------------------------
        var teamsToAssign = teams.Where(t => !teamEmojiMap.ContainsKey(t.Name)).ToList();

        // Pokud nebylo nic smazáno a není co přidat, končíme (šetříme zápis na disk)
        if (teamsToRemove.Count == 0 && teamsToAssign.Count == 0)
        {
            sb.Append(" -> Žádné změny nejsou potřeba. Vše je aktuální.");
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Info); // Info stačí, není to Success akce
            return;
        }

        // ---------------------------------------------------------
        // KROK C: KONTROLA DOSTUPNOSTI EMOJI
        // ---------------------------------------------------------
        // Zjistíme, které emoji jsou TEĎ obsazené (už po promazání, takže jsme recyklovali)
        var occupiedEmojis = new HashSet<string>(teamEmojiMap.Values);

        // Získáme seznam volných
        var availableEmojiList = allAvailableEmojis.Where(e => !occupiedEmojis.Contains(e)).ToList();

        if (teamsToAssign.Count > availableEmojiList.Count)
        {
            // --- KRITICKÁ CHYBA ---
            int missingCount = teamsToAssign.Count - availableEmojiList.Count;
            string errorMsg = $"CRITICAL: Nedostatek emoji! Potřeba: {teamsToAssign.Count}, K dispozici: {availableEmojiList.Count}. Chybí {missingCount} ks.";

            sb.AppendLine();
            sb.AppendLine($" -> ❌ {errorMsg}");
            sb.Append(" -> Synchronizace byla přerušena, změny nebyly uloženy.");

            // Logujeme chybu
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Error);
            throw new InvalidOperationException(errorMsg);
        }

        // ---------------------------------------------------------
        // KROK D: PŘIŘAZENÍ (Assignment)
        // ---------------------------------------------------------
        for (int i = 0; i < teamsToAssign.Count; i++)
        {
            var team = teamsToAssign[i];
            var emoji = availableEmojiList[i];

            teamEmojiMap.Add(team.Name, emoji);
            sb.AppendLine($" -> Přiřazeno: {team.Name} = {emoji}");
        }

        // ---------------------------------------------------------
        // KROK E: ULOŽENÍ A SOUHRN
        // ---------------------------------------------------------
        await SaveEmojiMapAsync();

        sb.AppendLine(); // Prázdný řádek pro oddělení
        sb.Append($" -> Hotovo. Smazáno: {teamsToRemove.Count}, Přidáno: {teamsToAssign.Count}.");

        await _logger.Consol(sb.ToString(), Logger.LogLevel.Success);
    }


    private async Task SendAdminAlert(string message)
    {
        // Získání kanálu pomocí ID je nejspolehlivější
        var channel = _client.GetChannel(adminAlertChannelId) as IMessageChannel;

        if (channel != null)
        {
            // Odeslání zprávy, ideálně s označením administrátora (@admin role nebo @tvůj nick)
            await channel.SendMessageAsync($"⚠️ **KRITICKÁ CHYBA BOTA** ⚠️\n<@{adminUserId}>\n\n{message}");
        }
        else
        {
            // Pokud se nepodaří najít ani kanál pro chybové hlášky
            await _logger.Consol($"CHYBA: Nepodařilo se najít kanál pro odesílání admin alertů (ID: {adminAlertChannelId}). Zpráva: {message}", Logger.LogLevel.Error);
        }
    }

    #region LOAD/SAVE

    private async Task LoadTeamsAsync()
    {
        teams = new List<Team>();
        var sb = new StringBuilder();
        sb.AppendLine("(LOAD TEAMS) Spouštím načítání seznamu týmů...");

        try
        {
            if (!File.Exists(teamsFilePath))
            {
                sb.Append(" -> Soubor s týmy neexistuje. Bude inicializován prázdný seznam.");
                await _logger.Consol(sb.ToString(), Logger.LogLevel.Warning);
                return;
            }

            string json = await File.ReadAllTextAsync(teamsFilePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                sb.Append(" -> Soubor je prázdný nebo obsahuje jen bílé znaky.");
                await _logger.Consol(sb.ToString(), Logger.LogLevel.Warning);
                return;
            }

            var loadedTeams = System.Text.Json.JsonSerializer.Deserialize<List<Team>>(json);

            teams = loadedTeams ?? new List<Team>();

            if (teams.Count > 0)
            {
                int assignedCount = 0;
                foreach (var team in teams)
                {
                    if (!teamEmojiMap.ContainsKey(team.Name))
                    {
                        await AssignNewEmoji(team.Name);
                        assignedCount++;
                    }
                }
                sb.Append($" -> Úspěšně načteno {teams.Count} týmů.");
                sb.Append($" -> Nových emoji přiřazeno: {assignedCount}.");
                await _logger.Consol(sb.ToString(), Logger.LogLevel.Debug);
            }
            else
            {
                sb.Append($" -> Soubor byl platný, ale nenašel žádné týmy.");
                await _logger.Consol(sb.ToString(), Logger.LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            sb.Append($" -> ❌ KRITICKÁ CHYBA: Načítání nebo deserializace selhala: {ex.Message}. Byla inicializována prázdná mapa.");
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Error);
        }
    }

    private async Task SaveTeamsAsync()
    {
        var sb = new StringBuilder();

        try
        {
            sb.AppendLine("(SAVE TEAMS) Spouštím ukládání seznamu týmů do souboru...");

            var teamsToSave = new List<Team>(teams);
            sb.Append($" -> Počet týmů k uložení: {teamsToSave.Count}");
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = System.Text.Json.JsonSerializer.Serialize(teamsToSave, options);

            await File.WriteAllTextAsync(teamsFilePath, json);
            sb.Append(" -> Seznam týmů byl úspěšně uložen.");
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Success);
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.Append($" -> Chyba při ukládání týmů: {ex.Message}");
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Error);
        }
    }

    private async Task LoadEmojiMapAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("(LOAD EMOJI) Spouštím načítání emoji mapy...");

        try
        {
            if (File.Exists(emojiFilePath))
            {
                string json = await File.ReadAllTextAsync(emojiFilePath);
                teamEmojiMap = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            else
            {
                sb.Append(" -> Soubor s mapou neexistuje.");
            }

            if (teamEmojiMap.Count > 0)
            {
                sb.Append($" -> Úspěšně načteno {teamEmojiMap.Count} položek.");
                await _logger.Consol(sb.ToString(), Logger.LogLevel.Success);
            }
            else
            {
                sb.Append(" -> Mapa je prázdná (připravena k použití).");
                await _logger.Consol(sb.ToString(), Logger.LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.Append($" -> Chyba při načítání (JSON je poškozený?): {ex.Message}");
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Error);
        }
    }

    private async Task SaveEmojiMapAsync()
    {
        var sb = new StringBuilder();

        try
        {
            sb.AppendLine("(SAVE EMOJI) Spouštím ukládání emoji mapy do souboru...");
            var dataToSave = new Dictionary<string, string>(teamEmojiMap);
            sb.Append($" -> Počet položek k uložení: {dataToSave.Count}");
            string json = JsonConvert.SerializeObject(dataToSave, Formatting.Indented);
            await File.WriteAllTextAsync(emojiFilePath, json);
            sb.Append(" -> Emoji mapa byla úspěšně uložena.");
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Success);
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.Append($" -> KRITICKÁ CHYBA při ukládání: {ex.Message}");
            await _logger.Consol(sb.ToString(), Logger.LogLevel.Error);
        }
    }
    #endregion
}
