using BotManager.Backend.Bots.Models;
using Discord;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Builds the Discord payloads (embed, message components, modals) of the board message
    /// and owns the custom IDs used to route board interactions back to the session.
    /// </summary>
    internal static class BoardComponentsBuilder
    {
        /// <summary>
        /// Prefix used in button custom IDs for team toggle interactions.
        /// </summary>
        internal const string TeamTogglePrefix = "team_toggle:";

        /// <summary>
        /// Prefix used in select-menu custom IDs for team toggle interactions.
        /// </summary>
        internal const string TeamSelectPrefix = "team_select:";

        /// <summary>
        /// Prefix used in button custom IDs for board admin action buttons.
        /// </summary>
        internal const string BoardActionPrefix = "board_action:";

        /// <summary>Custom ID of the Add Team admin button.</summary>
        internal const string BoardAddTeamActionId = "board_action:add_team";

        /// <summary>Custom ID of the Refresh Board admin button.</summary>
        internal const string BoardRefreshActionId = "board_action:refresh";

        /// <summary>Prefix used in modal custom IDs for board modal submissions.</summary>
        internal const string BoardModalPrefix = "board_modal:";

        /// <summary>Custom ID of the Add Team modal.</summary>
        internal const string BoardAddTeamModalId = "board_modal:add_team";

        /// <summary>Number of action rows used for team select menus (rows 0-3).</summary>
        internal const int MaxTeamMenus = 4;

        /// <summary>Maximum number of options Discord allows in a single select menu.</summary>
        internal const int MaxOptionsPerMenu = 25;

        /// <summary>Maximum number of teams that can be shown on the board message (100).</summary>
        internal const int MaxTeamsTotal = MaxTeamMenus * MaxOptionsPerMenu;

        /// <summary>Row index reserved for the admin action buttons.</summary>
        internal const int AdminActionRow = 4;

        private const int MaxTeamNameValueLength = 100;
        private const int MaxLabelLength = 100;

        /// <summary>
        /// Builds the message components for the board message.
        /// Rows 0-3 hold team select menus (up to 100 teams total).
        /// Row 4 is reserved for admin action buttons (Add Team, Refresh Board).
        /// Picking a team from a menu toggles the user's membership in that team role.
        /// </summary>
        internal static ComponentBuilder BuildBoardComponents(BoardMessageDto boardMessage)
        {
            var builder = new ComponentBuilder();

            var entries = boardMessage.Entries.Take(MaxTeamsTotal).ToList();
            var menuCount = (int)Math.Ceiling(entries.Count / (double)MaxOptionsPerMenu);

            for (var menuIndex = 0; menuIndex < menuCount; menuIndex++)
            {
                var menuEntries = entries
                    .Skip(menuIndex * MaxOptionsPerMenu)
                    .Take(MaxOptionsPerMenu)
                    .ToList();

                var select = new SelectMenuBuilder()
                    .WithCustomId($"{TeamSelectPrefix}{menuIndex}")
                    .WithPlaceholder($"Choose your team ({menuIndex + 1}/{menuCount})")
                    .WithMinValues(1)
                    .WithMaxValues(1);

                foreach (var entry in menuEntries)
                {
                    var teamValue = entry.Title.Length > MaxTeamNameValueLength
                        ? entry.Title[..MaxTeamNameValueLength]
                        : entry.Title;
                    var label = entry.Title.Length > MaxLabelLength
                        ? entry.Title[..MaxLabelLength]
                        : entry.Title;

                    var option = new SelectMenuOptionBuilder()
                        .WithLabel(label)
                        .WithValue(teamValue);

                    if (!string.IsNullOrWhiteSpace(entry.Emoji) && Emoji.TryParse(entry.Emoji.Trim(), out var parsedEmoji))
                    {
                        option.WithEmote(parsedEmoji);
                    }

                    select.AddOption(option);
                }

                builder.WithSelectMenu(select, row: menuIndex);
            }

            // Admin action buttons request row 4. Discord.Net appends a new row when the requested
            // row does not exist yet, so with fewer than 4 menus the buttons land on rows after the menus.
            builder.WithButton(new ButtonBuilder()
                .WithLabel("Add Team")
                .WithCustomId(BoardAddTeamActionId)
                .WithStyle(ButtonStyle.Success)
                .WithEmote(new Emoji("➕")), row: AdminActionRow);

            builder.WithButton(new ButtonBuilder()
                .WithLabel("Refresh Board")
                .WithCustomId(BoardRefreshActionId)
                .WithStyle(ButtonStyle.Secondary)
                .WithEmote(new Emoji("🔄")), row: AdminActionRow);

            return builder;
        }

        /// <summary>
        /// Builds the Discord modal shown when an admin clicks the Add Team button.
        /// </summary>
        internal static Modal BuildAddTeamModal()
        {
            return new ModalBuilder()
                .WithTitle("Add New Team")
                .WithCustomId(BoardAddTeamModalId)
                .AddTextInput("Team Name", "team_name", TextInputStyle.Short,
                    placeholder: "e.g. Alpha Squad", required: true, maxLength: 100)
                .AddTextInput("Leader Name", "leader_name", TextInputStyle.Short,
                    placeholder: "e.g. John Doe", required: true, maxLength: 100)
                .AddTextInput("Contact", "contact_info", TextInputStyle.Short,
                    placeholder: "e.g. @johndoe or #channel", required: false, maxLength: 100)
                .Build();
        }

        /// <summary>
        /// Builds the embed payload used for board message publishing.
        /// </summary>
        internal static EmbedBuilder BuildBoardEmbed(BoardMessageDto boardMessage)
        {
            var embed = new EmbedBuilder()
                .WithTitle(string.IsNullOrWhiteSpace(boardMessage.Title) ? "📋 Seznam položek" : boardMessage.Title)
                .WithColor(Color.Blue)
                .WithFooter($"Aktualizováno: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

            if (!string.IsNullOrWhiteSpace(boardMessage.Description))
            {
                embed.WithDescription(boardMessage.Description);
            }

            foreach (var entry in boardMessage.Entries)
            {
                var emoji = string.IsNullOrWhiteSpace(entry.Emoji) ? string.Empty : entry.Emoji.Trim() + " ";
                var details = string.IsNullOrWhiteSpace(entry.Details) ? "​" : entry.Details.Trim();
                // Add one visual spacer line between teams.
                embed.AddField($"{emoji}{entry.Title}", $"{details}\n​", inline: false);
            }

            return embed;
        }
    }
}
