using BotManager.Backend.Shared.Models;
using Discord;
using Discord.WebSocket;

namespace BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers
{
    /// <summary>
    /// Resolves and creates the Discord roles that back board teams.
    /// Teams are bound to their role by id; name matching is only a legacy fallback and never
    /// returns roles that carry elevated permissions, are managed by an integration, or sit above the bot.
    /// </summary>
    internal static class TeamRoleResolver
    {
        /// <summary>Generic user-facing error text that does not leak exception details.</summary>
        internal const string GenericErrorText = "Nastala interní chyba, zkuste to prosím později.";

        private static readonly GuildPermission[] ElevatedPermissions =
        {
            GuildPermission.Administrator,
            GuildPermission.ManageGuild,
            GuildPermission.ManageRoles,
            GuildPermission.ManageChannels,
            GuildPermission.ManageMessages,
            GuildPermission.ManageWebhooks,
            GuildPermission.ManageNicknames,
            GuildPermission.ManageEmojisAndStickers,
            GuildPermission.ManageEvents,
            GuildPermission.ManageThreads,
            GuildPermission.KickMembers,
            GuildPermission.BanMembers,
            GuildPermission.ModerateMembers,
            GuildPermission.MentionEveryone,
            GuildPermission.ViewAuditLog,
            GuildPermission.MoveMembers,
            GuildPermission.MuteMembers,
            GuildPermission.DeafenMembers
        };

        /// <summary>
        /// Returns whether a role may be granted or modified by the board on behalf of regular users.
        /// </summary>
        public static bool IsSafeTeamRole(SocketGuild guild, IRole role)
        {
            if (role.Id == guild.EveryoneRole.Id || role.IsManaged)
            {
                return false;
            }

            if (ElevatedPermissions.Any(permission => role.Permissions.Has(permission)))
            {
                return false;
            }

            var botHierarchy = guild.CurrentUser?.Hierarchy ?? int.MaxValue;
            return role.Position < botHierarchy;
        }

        /// <summary>
        /// Finds the role bound to a team. Legacy teams without a stored role id fall back to a
        /// name match, but only for safe roles.
        /// </summary>
        public static SocketRole? FindTeamRole(SocketGuild guild, TeamDto team)
        {
            if (team.RoleId is ulong roleId)
            {
                var boundRole = guild.GetRole(roleId);
                return boundRole != null && IsSafeTeamRole(guild, boundRole) ? boundRole : null;
            }

            return guild.Roles.FirstOrDefault(r =>
                r.Name.Equals(team.Name, StringComparison.Ordinal) && IsSafeTeamRole(guild, r));
        }

        /// <summary>
        /// Finds the team role or creates a new permission-less role, and persists the binding.
        /// </summary>
        public static async Task<IRole?> FindOrCreateTeamRoleAsync(SocketGuild guild, TeamDto team, IPluginContext context)
        {
            IRole? role = FindTeamRole(guild, team);
            if (role == null)
            {
                role = await CreateTeamRoleAsync(guild, team.Name);
                context.Logger.LogInformation("BotId={BotId}: Created role {RoleId} for team '{TeamName}'",
                    context.Bot.BotId, role.Id, team.Name);
            }

            if (team.RoleId != role.Id && team.TeamId is int teamId)
            {
                team.RoleId = role.Id;
                await context.TeamsDataService.SetRoleIdAsync(teamId, role.Id);
            }

            return role;
        }

        /// <summary>
        /// Creates a new team role without any permissions.
        /// </summary>
        public static async Task<IRole> CreateTeamRoleAsync(SocketGuild guild, string teamName)
        {
            var randomColor = new Color((uint)Random.Shared.Next(0x1000000));
            return await guild.CreateRoleAsync(teamName, permissions: GuildPermissions.None, color: randomColor,
                isHoisted: false, isMentionable: false);
        }

        /// <summary>
        /// Returns whether a guild user may run board admin actions (administrator or guild owner).
        /// </summary>
        public static bool IsBoardAdmin(IUser user, SocketGuild guild)
        {
            return user is SocketGuildUser guildUser
                   && (guildUser.GuildPermissions.Administrator || guildUser.Id == guild.OwnerId);
        }
    }
}
