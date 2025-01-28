using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Color = Discord.Color;

class Program
{
    private DiscordSocketClient _client;
    private const string FilePath = "teams.json"; // Cesta k souboru
    private List<Team> teams = new List<Team>(); // Seznam týmů

    public class BotData
    {
        public ulong? TeamsMessageId { get; set; }
    }

    private readonly string dataFilePath = "botdata.json";  // Cesta k souboru

    private async Task SaveBotDataAsync(BotData botData)
    {
        var json = JsonConvert.SerializeObject(botData);
        await File.WriteAllTextAsync(dataFilePath, json);
    }

    private async Task<BotData> LoadBotDataAsync()
    {
        if (File.Exists(dataFilePath))
        {
            var json = await File.ReadAllTextAsync(dataFilePath);
            return JsonConvert.DeserializeObject<BotData>(json);
        }
        return new BotData();  // Pokud soubor neexistuje, vrátíme nový objekt s prázdným ID
    }

    static async Task Main(string[] args) => await new Program().RunBotAsync();
    public async Task RunBotAsync()
    {
        // Přidání intents
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
        };

        _client = new DiscordSocketClient(config);

        _client.Log += Log;
        _client.MessageReceived += HandleMessageAsync;

        // Načtení seznamu týmů ze souboru
        LoadTeams();

        // Nahraď svůj token zde
        string botToken = "MTMzMzg4NjI5ODgyMTQ5NjkyNw.GHw_VE.79Fm7vudy3voePPrcq9UxdqS7XBvaJekPkGnYI";

        await _client.LoginAsync(TokenType.Bot, botToken);
        await _client.StartAsync();

        Console.WriteLine("Bot je spuštěn!");
        await Task.Delay(-1); // Keep the program running
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
                    teams.Add(new Team { Name = teamName, Leader = leaderName, Contact = contact });
                    SaveTeams();
                    await message.Author.SendMessageAsync($"Tým **{teamName}** byl přidán do seznamu!");
                    await UpdateTeamsList(channel);  // Aktualizujeme seznam týmů v existující zprávě
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


    // Načítání seznamu týmů ze souboru
    private void LoadTeams()
    {
        if (File.Exists(FilePath))
        {
            string json = File.ReadAllText(FilePath);
            teams = System.Text.Json.JsonSerializer.Deserialize<List<Team>>(json) ?? new List<Team>();
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
}

// Třída Team pro ukládání dat o týmu
public class Team
{
    public string Name { get; set; }
    public string Leader { get; set; }
    public string Contact { get; set; }
}
