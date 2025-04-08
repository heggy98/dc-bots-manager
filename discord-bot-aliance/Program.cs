using Discord;
using Discord.WebSocket;
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
using Color = Discord.Color;

class Program
{
    private DiscordSocketClient _client;
    private const string FilePath = "teams.json"; // Cesta k souboru
    private readonly string emojiFilePath = "emojis.json";
    private readonly string dataFilePath = "botdata.json";  // Cesta k souboru
    private ulong? reactionMessageId;
    private Dictionary<string, string> teamEmojiMap = new Dictionary<string, string>();
    private List<Team> teams = new List<Team>(); // Seznam týmů

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
                var channelName = "✅┃vyber-tym";
                var channel = guild.TextChannels.FirstOrDefault(c => c.Name == channelName);
                if (channel != null)
                {
                    await EnsureRoleSelectionMessage(guild, channel);
                    await EnsureTeamsRolesExist(guild);
                }
                else
                {
                    Console.WriteLine($"Channel s nazvem {channelName} nebyla nalezena");
                }
            }
        };
        _client.Log += Log;
        _client.MessageReceived += HandleMessageAsync;
        _client.ReactionAdded += HandleReactionAddedAsync;
        _client.ReactionRemoved += HandleReactionRemovedAsync;

        // Načtení seznamu týmů ze souboru
        LoadTeams();
        LoadEmojiMap();

        // Nahraď svůj token zde
        string botToken = "MTMzMzg4NjI5ODgyMTQ5NjkyNw.GHw_VE.79Fm7vudy3voePPrcq9UxdqS7XBvaJekPkGnYI";

        await _client.LoginAsync(TokenType.Bot, botToken);
        await _client.StartAsync();

        Console.WriteLine("Bot je spuštěn!");
        await Task.Delay(-1); // Keep the program running
    }

    private async Task SaveBotDataAsync(BotData botData)
    {
        Console.WriteLine("Ukladam bot data..");
        var json = JsonConvert.SerializeObject(botData);
        await File.WriteAllTextAsync(dataFilePath, json);
    }

    private async Task EnsureRoleSelectionMessage(SocketGuild guild, ITextChannel channel)
    {
        var botData = await LoadBotDataAsync();

        if (botData.RoleMessageId != null)
        {
            // Zkusíme najít starou zprávu
            var oldMessage = await channel.GetMessageAsync(botData.RoleMessageId.Value) as IUserMessage;
            if (oldMessage != null)
            {
                Console.WriteLine("Zpráva pro výběr role už existuje, nepřidávám novou.");
                reactionMessageId = botData.RoleMessageId; // Načteme ID zprávy
                return;
            }
        }

        // Pokud zpráva neexistuje, vytvoříme ji
        Console.WriteLine("Zpráva neexistuje, vytvářím novou...");
        await SendRoleSelectionMessage(guild, channel);
    }

    private async Task SendRoleSelectionMessage(SocketGuild guild, ITextChannel channel)
    {
        var embed = new EmbedBuilder()
        {
            Title = "Vyber si svůj tým! ⚔️",
            Description = "Reaguj na odpovídající emoji a získáš roli svého týmu.\n\n",
            Color = Color.Green
        };

        var teamEmojis = new Dictionary<string, string>(); // Mapování tým -> emoji
        string[] emojis = { "⚔️", "🏹", "🛡️", "🔥", "⚡", "💀", "🌿", "❄️", "🔮", "🦅" }; // Náhodná emoji
        int i = 0;

        foreach (var team in teams)
        {
            string emoji = emojis[i % emojis.Length]; // Přiřazení emoji
            teamEmojis[team.Name] = emoji;
            embed.Description += $"{emoji} - **{team.Name}**\n";
            i++;
        }

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

        Console.WriteLine("Zpráva pro výběr týmu byla odeslána!");
    }


    private async Task<BotData> LoadBotDataAsync()
    {
        if (File.Exists(dataFilePath))
        {
            Console.WriteLine("Našel jsem bot data.. parsuju..");
            try
            {
                var json = await File.ReadAllTextAsync(dataFilePath);
                return JsonConvert.DeserializeObject<BotData>(json);
            }
            catch (Exception)
            {
                Console.WriteLine("Chyba při parsovani.. kurva");
                throw;
            }
        }
        return new BotData();  // Pokud soubor neexistuje, vrátíme nový objekt s prázdným ID
    }



    private async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheable1, Cacheable<IMessageChannel, ulong> cacheable2, SocketReaction reaction)
    {
        if (reaction.User.IsSpecified && !reaction.User.Value.IsBot)
        {
            var guild = (reaction.Channel as SocketGuildChannel)?.Guild;
            if (guild == null) return;

            var user = guild.GetUser(reaction.UserId);
            if (user == null) return;

            if (reactionMessageId != null && reaction.MessageId == reactionMessageId)
            {
                foreach (var entry in teamEmojiMap)
                {
                    if (reaction.Emote.Name == entry.Value)
                    {
                        var newRole = guild.Roles.FirstOrDefault(r => r.Name == entry.Key);
                        if (newRole == null) continue;

                        // Najdeme všechny týmové role, které má uživatel (může být jen jedna, ale jistota je jistota)
                        var teamRoles = user.Roles.Where(r => teamEmojiMap.ContainsKey(r.Name)).ToList();

                        // Odebereme staré týmové role
                        foreach (var role in teamRoles)
                        {
                            if (role.Id != newRole.Id)
                            {
                                await user.RemoveRoleAsync(role);
                                Console.WriteLine($"Uživatel {user.Username} byl odebrán z týmu {role.Name}.");
                            }
                        }

                        // Přidáme novou roli, pokud ji ještě nemá
                        if (!user.Roles.Contains(newRole))
                        {
                            await user.AddRoleAsync(newRole);
                            await user.SendMessageAsync($"Byla ti přiřazena role **{newRole.Name}**!");
                            Console.WriteLine($"Uživatel {user.Username} si vybral tým {newRole.Name}.");
                        }

                        break; // už jsme našli odpovídající emoji
                    }
                }
            }
        }
    }

    private async Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> cacheable1, Cacheable<IMessageChannel, ulong> cacheable2, SocketReaction reaction)
    {
        if (reaction.User.IsSpecified && !reaction.User.Value.IsBot)
        {
            var guild = (reaction.Channel as SocketGuildChannel)?.Guild;
            if (guild == null) return;

            var user = guild.GetUser(reaction.UserId);
            if (user == null) return;

            if (reactionMessageId != null && reactionMessageId == reaction.MessageId)
            {
                foreach (var entry in teamEmojiMap)
                {
                    if (reaction.Emote.Name == entry.Value)
                    {
                        var role = guild.Roles.FirstOrDefault(r => r.Name == entry.Key);
                        if (role != null && user.Roles.Contains(role))
                        {
                            await user.RemoveRoleAsync(role);
                            await user.SendMessageAsync($"Byla ti odebrána role **{role.Name}**.");
                            Console.WriteLine($"Uživatel {user.Username} si odebral tým {role.Name}.");
                        }
                    }
                }
            }
        }
    }

    private async Task UpdateReactionRoleMessage(SocketTextChannel channel)
    {
        if (reactionMessageId == null)
        {
            // Vytvoření nové embed zprávy
            var embed = new EmbedBuilder
            {
                Title = "Vyber si tým reakcí!",
                Color = Color.Orange,
                Description = string.Join("\n", teamEmojiMap.Select(kvp => $"{kvp.Value} → **{kvp.Key}**"))
            };

            var msg = await channel.SendMessageAsync(embed: embed.Build());

            reactionMessageId = msg.Id;
            await SaveBotDataAsync(new BotData { TeamsMessageId = teamsMessageId, RoleMessageId = reactionMessageId });

            // Přidání emoji jako reakcí
            foreach (var emoji in teamEmojiMap.Values)
            {
                await msg.AddReactionAsync(new Emoji(emoji));
            }
        }
        else
        {
            var message = await channel.GetMessageAsync(reactionMessageId.Value) as IUserMessage;
            if (message != null)
            {
                var embed = new EmbedBuilder
                {
                    Title = "Vyber si tým reakcí!",
                    Color = Color.Orange,
                    Description = string.Join("\n", teamEmojiMap.Select(kvp => $"{kvp.Value} → **{kvp.Key}**"))
                };

                await message.ModifyAsync(m => m.Embed = embed.Build());

                // Vymaž staré a přidej aktuální reakce
                var existingReactions = message.Reactions.Select(kv => kv.Key.Name).ToList();
                foreach (var reaction in existingReactions)
                {
                    await message.RemoveAllReactionsForEmoteAsync(new Emoji(reaction));
                }

                foreach (var emoji in teamEmojiMap.Values)
                {
                    await message.AddReactionAsync(new Emoji(emoji));
                }

                Console.WriteLine("Zpráva s emoji byla aktualizována.");
            }
        }
    }

    private ulong? teamsMessageId = null;

    private async Task HandleMessageAsync(SocketMessage message)
    {
        // Ignoruj zprávy od botů
        if (message.Author.IsBot) return;

        // Log do konzole, abys viděl, jestli bot zprávu registruje
        Console.WriteLine($"Zpráva od {message.Author.Username}: {message.Content}");

        var channel = message.Channel as SocketTextChannel;

        if (channel != null)
        {
            if (teamsMessageId == null)
            {
                // Načítáme `teamsMessageId` při spuštění
                var botData = await LoadBotDataAsync();
                teamsMessageId = botData.TeamsMessageId;
            }

            // Příkaz: Přidání týmu
            if (message.Content.StartsWith("/pridat-tym"))
            {
                string[] parts = message.Content.Replace("/pridat-tym", "").Trim().Split('|');
                if (parts.Length < 2)
                {
                    await message.Author.SendMessageAsync("Prosím, zadej název týmu a jméno velitele. Použití: `/pridat-tym NázevTýmu | Velitel | [Kontakt]`");
                    return;
                }

                string teamName = parts[0].Trim();
                string leaderName = parts[1].Trim();
                string contact = parts.Length > 2 ? parts[2].Trim() : "N/A";

                if (teams.Any(t => t.Name == teamName))
                {
                    await message.Author.SendMessageAsync($"Tým **{teamName}** už je v seznamu!");
                }
                else
                {
                    var team = new Team { Name = teamName, Leader = leaderName, Contact = contact };
                    teams.Add(team);
                    SaveTeams();
                    var guild = (message.Channel as SocketGuildChannel)?.Guild;
                    if (guild != null)
                    {
                        // Vytvoření nové role
                        await CreateRole(guild, team);
                    }
                    AssignNewEmoji(teamName);
                    await message.Author.SendMessageAsync($"Tým **{teamName}** byl přidán do seznamu!");
                    await UpdateTeamsList(channel);  // Aktualizujeme seznam týmů v existující zprávě
                    await UpdateReactionRoleMessage(channel);
                }

                await message.DeleteAsync();    
            }

            // Příkaz: Odebrání týmu
            if (message.Content.StartsWith("/odebrat-tym"))
            {
                string teamName = message.Content.Replace("/odebrat-tym", "").Trim();

                var team = teams.FirstOrDefault(t => t.Name == teamName);
                if (team != null)
                {
                    teams.Remove(team);
                    SaveTeams();
                    await message.Author.SendMessageAsync($"Tým **{teamName}** byl odebrán ze seznamu!");
                    await UpdateTeamsList(channel);  // Aktualizujeme seznam týmů v existující zprávě
                    await UpdateReactionRoleMessage(channel);
                }
                else
                {
                    await message.Author.SendMessageAsync($"Tým **{teamName}** se v seznamu nenachází.");
                }

                await message.DeleteAsync();
            }

            // Příkaz: Zobrazení seznamu týmů
            if (message.Content == "/seznam-tymu")
            {
                await ShowTeamsList(channel); // Zobrazíme seznam týmů
                await message.DeleteAsync();
            }
        }
    }

    private void AssignNewEmoji(string teamName)
    {
        string emoji = GetRandomEmoji();
        while (teamEmojiMap.ContainsValue(emoji)) // nedublovat emoji
        {
            emoji = GetRandomEmoji();
        }

        teamEmojiMap[teamName] = emoji;
        SaveEmojiMap();
    }

    private async Task ShowTeamsList(ITextChannel channel)
    {
        if (teamsMessageId == null)
        {
            // Vytvoření nové zprávy se seznamem týmů
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

            embed.Description = teamsList;

            // Poslání nové zprávy a uložení jejího ID
            var message = await channel.SendMessageAsync(embed: embed.Build());
            teamsMessageId = message.Id;

            // Uložíme ID zprávy do souboru
            await SaveBotDataAsync(new BotData { TeamsMessageId = teamsMessageId });
        }
        else
        {
            // Pokud už máme zprávu, jen ji upravíme
            await UpdateTeamsList(channel);
        }
    }

    private async Task UpdateTeamsList(ITextChannel channel)
    {
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

                embed.Description = teamsList;

                // Upravení existující zprávy
                await message.ModifyAsync(msg => msg.Embed = embed.Build());

                // Uložení ID zprávy po každé změně
                await SaveBotDataAsync(new BotData { TeamsMessageId = teamsMessageId });
            }
        }
    }

    private async Task EnsureTeamsRolesExist(SocketGuild guild)
    {
        foreach (var team in teams)
        {
            // Zkontrolujeme, jestli role s názvem týmu už existuje
            var existingRole = guild.Roles.FirstOrDefault(r => r.Name == team.Name);
            if (existingRole == null)
            {
                // Pokud neexistuje, vytvoříme ji
                await CreateRole(guild, team);
            }
        }
    }

    private static async Task CreateRole(SocketGuild guild, Team team)
    { 
        Console.WriteLine("Vytvářím roli " +  team.Name);
        var randomizer = new Random();
        var randomColor = new Color(randomizer.Next(256), randomizer.Next(256), randomizer.Next(256));
        var newRole = await guild.CreateRoleAsync(team.Name, GuildPermissions.None, randomColor, false, true);
    }

    private Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    private async Task DisplayTeams(ITextChannel channel)
    {
        // Vytvoříme embed pro seznam týmů
        var embed = new EmbedBuilder()
        {
            Title = "Seznam týmů",
            Color = Color.Blue // Můžeš změnit barvu embedu
        };

        // Přidáme tabulku týmů do embed zprávy
        string teamsList = "Název týmu | Velitel | Kontakt\n" +
                           "-------------------------\n";

        foreach (var team in teams)
        {
            teamsList += $"{team.Name} | {team.Leader} | {team.Contact}\n";
        }

        // Přidání textu tabulky do embedu
        embed.Description = teamsList;

        // Odeslání embed zprávy do kanálu
        await channel.SendMessageAsync(embed: embed.Build());
    }

    private string GetRandomEmoji()
    {
        string[] availableEmojis = { "⚔️", "🏹", "🛡️", "🔥", "⚡", "💀", "🌿", "❄️", "🔮", "🦅" }; // Náhodná emoji
        var random = new Random();
        return availableEmojis[random.Next(availableEmojis.Length)];
    }

    #region LOAD/SAVE

    // Načítání seznamu týmů ze souboru
    private void LoadTeams()
    {
        if (File.Exists(FilePath))
        {
            string json = File.ReadAllText(FilePath);
            teams = System.Text.Json.JsonSerializer.Deserialize<List<Team>>(json) ?? new List<Team>();
            foreach (var team in teams)
            {
                if (!teamEmojiMap.ContainsKey(team.Name))
                {
                    // Např. každému týmu přidáme náhodný emoji
                    teamEmojiMap[team.Name] = GetRandomEmoji();
                }
            }

            Console.WriteLine("Seznam týmů byl načten.");
        }
        else
        {
            Console.WriteLine("Soubor s týmy neexistuje, vytvářím nový seznam.");
            teams = new List<Team>();
        }
    }

    // Ukládání seznamu týmů do souboru
    private void SaveTeams()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(teams, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
        Console.WriteLine("Seznam týmů byl uložen.");
    }

    private void LoadEmojiMap()
    {
        if (File.Exists(emojiFilePath))
        {
            string json = File.ReadAllText(emojiFilePath);
            teamEmojiMap = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            Console.WriteLine("Emoji mapa byla načtena.");
        }
        else
        {
            Console.WriteLine("Emoji mapa neexistuje, bude vytvořena nová.");
            teamEmojiMap = new Dictionary<string, string>();
        }
    }

    private void SaveEmojiMap()
    {
        string json = JsonConvert.SerializeObject(teamEmojiMap, Formatting.Indented);
        File.WriteAllText(emojiFilePath, json);
        Console.WriteLine("Emoji mapa byla uložena.");
    }
    #endregion
}
