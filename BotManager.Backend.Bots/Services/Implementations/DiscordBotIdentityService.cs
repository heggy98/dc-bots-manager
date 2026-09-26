using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BotManager.Backend.Bots.Services.Implementations
{
    /// <summary>
    /// Retrieves Discord bot identity and guild metadata via Discord REST APIs.
    /// Results are cached per token (keyed by its hash) so dashboard/public requests do not
    /// hit Discord rate limits.
    /// </summary>
    public class DiscordBotIdentityService
    {
        private static readonly TimeSpan IdentityCacheDuration = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan GuildsCacheDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(1);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DiscordBotIdentityService> _logger;
        private readonly IMemoryCache? _cache;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Creates a new Discord bot identity service.
        /// </summary>
        public DiscordBotIdentityService(IHttpClientFactory httpClientFactory, ILogger<DiscordBotIdentityService> logger,
            IMemoryCache? cache = null)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Gets guild names visible to a bot token (cached).
        /// </summary>
        public async Task<List<string>> GetGuildNamesAsync(string botToken, CancellationToken cancellationToken = default)
        {
            if (_cache == null || string.IsNullOrWhiteSpace(botToken))
            {
                return await FetchGuildNamesAsync(botToken, cancellationToken);
            }

            var key = CacheKey("guilds", botToken);
            if (_cache.TryGetValue(key, out List<string>? cached) && cached != null)
            {
                return [.. cached];
            }

            var guilds = await FetchGuildNamesAsync(botToken, cancellationToken);
            _cache.Set(key, guilds, guilds.Count > 0 ? GuildsCacheDuration : FailureCacheDuration);
            return [.. guilds];
        }

        /// <summary>
        /// Gets display identity information for a bot token (cached, including failures for a short time).
        /// </summary>
        public async Task<DiscordBotIdentity?> GetIdentityAsync(string botToken, CancellationToken cancellationToken = default)
        {
            if (_cache == null || string.IsNullOrWhiteSpace(botToken))
            {
                return await FetchIdentityAsync(botToken, cancellationToken);
            }

            var key = CacheKey("identity", botToken);
            if (_cache.TryGetValue(key, out CachedIdentity? cached) && cached != null)
            {
                return cached.Identity;
            }

            var identity = await FetchIdentityAsync(botToken, cancellationToken);
            _cache.Set(key, new CachedIdentity(identity), identity != null ? IdentityCacheDuration : FailureCacheDuration);
            return identity;
        }

        /// <summary>
        /// Validates a token directly against Discord, bypassing the cache.
        /// </summary>
        public async Task<bool> IsTokenAuthorizedAsync(string botToken, CancellationToken cancellationToken = default)
        {
            var identity = await FetchIdentityAsync(botToken, cancellationToken);
            if (identity != null && _cache != null)
            {
                _cache.Set(CacheKey("identity", botToken), new CachedIdentity(identity), IdentityCacheDuration);
            }

            return identity != null;
        }

        /// <summary>
        /// Builds a cache key from a hash of the normalized token (never the token itself).
        /// </summary>
        private static string CacheKey(string kind, string botToken)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeBotToken(botToken)));
            return $"discord-{kind}:{Convert.ToHexString(hash)}";
        }

        private sealed record CachedIdentity(DiscordBotIdentity? Identity);

        /// <summary>
        /// Fetches guild names visible to a bot token from Discord.
        /// </summary>
        private async Task<List<string>> FetchGuildNamesAsync(string botToken, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(botToken))
                return [];

            var normalizedToken = NormalizeBotToken(botToken);
            if (string.IsNullOrWhiteSpace(normalizedToken))
                return [];

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me/guilds");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bot", normalizedToken);

                var client = _httpClientFactory.CreateClient();
                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Discord guilds fetch failed with status code {StatusCode}", (int)response.StatusCode);
                    return [];
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var guilds = await JsonSerializer.DeserializeAsync<List<DiscordGuildResponse>>(stream, JsonOptions, cancellationToken);
                return guilds?.Where(g => !string.IsNullOrWhiteSpace(g.Name))
                              .Select(g => g.Name!)
                              .ToList() ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch Discord bot guilds");
                return [];
            }
        }

        /// <summary>
        /// Fetches display identity information for a bot token from Discord.
        /// </summary>
        private async Task<DiscordBotIdentity?> FetchIdentityAsync(string botToken, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(botToken))
            {
                return null;
            }

            var normalizedToken = NormalizeBotToken(botToken);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                return null;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bot", normalizedToken);

                var client = _httpClientFactory.CreateClient();
                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Discord profile fetch failed with status code {StatusCode}", (int)response.StatusCode);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var profile = await JsonSerializer.DeserializeAsync<DiscordProfileResponse>(stream, JsonOptions, cancellationToken);
                if (profile == null || string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Username))
                {
                    return null;
                }

                var displayName = string.IsNullOrWhiteSpace(profile.GlobalName) ? profile.Username : profile.GlobalName;
                var avatarUrl = BuildAvatarUrl(profile);

                return new DiscordBotIdentity
                {
                    Name = displayName,
                    AvatarUrl = avatarUrl
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch Discord bot identity");
                return null;
            }
        }

        /// <summary>
        /// Normalizes token input by trimming wrappers and optional Bot prefix.
        /// </summary>
        private static string NormalizeBotToken(string botToken)
        {
            var normalized = botToken.Trim().Trim('"', '\'');
            if (normalized.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(4).Trim();
            }

            return normalized;
        }

        /// <summary>
        /// Builds a CDN avatar URL for a Discord profile, falling back to default avatar.
        /// </summary>
        private static string BuildAvatarUrl(DiscordProfileResponse profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.Avatar))
            {
                return $"https://cdn.discordapp.com/avatars/{profile.Id}/{profile.Avatar}.png?size=128";
            }

            var defaultAvatarIndex = 0;
            if (int.TryParse(profile.Discriminator, out var discriminator) && discriminator > 0)
            {
                defaultAvatarIndex = discriminator % 5;
            }
            else if (ulong.TryParse(profile.Id, out var snowflakeId))
            {
                defaultAvatarIndex = (int)((snowflakeId >> 22) % 6);
            }

            return $"https://cdn.discordapp.com/embed/avatars/{defaultAvatarIndex}.png";
        }

        private class DiscordProfileResponse
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("username")]
            public string? Username { get; set; }

            [JsonPropertyName("global_name")]
            public string? GlobalName { get; set; }

            [JsonPropertyName("avatar")]
            public string? Avatar { get; set; }

            [JsonPropertyName("discriminator")]
            public string? Discriminator { get; set; }
        }

        private class DiscordGuildResponse
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }
    }

    public class DiscordBotIdentity
    {
        public string Name { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
    }
}
