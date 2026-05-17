using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Provides emoji catalog data using a cached external source with local fallback.
    /// </summary>
    public class EmojiCatalogService : IEmojiCatalogService
    {
        private const string CacheKey = "emoji-catalog";
        private static readonly string[] SourceUrls =
        {
            "https://raw.githubusercontent.com/github/gemoji/master/db/emoji.json",
            "https://cdn.jsdelivr.net/npm/emoji.json@13.1.0/emoji.json"
        };

        private static readonly string[] FallbackEmojis =
        {
            "⚔️", "🏹", "🛡️", "🔥", "❄️", "🦅", "🌿", "⚡", "💀", "🌑", "🌊", "🐺", "🎯", "🎖️", "⭐", "💥",
            "🚀", "🏆", "⚙️", "🔱", "🗡️", "🎪", "🎭", "🎸", "🧠", "🧩", "🛰️", "🛸", "🧨", "🐉", "🦊", "🦁",
            "🐯", "🐻", "🦈", "🦂", "🦇", "🛸", "💎", "🔮", "🧿", "📡", "🧪", "🧬", "🛰", "🎯", "🌟", "☄️"
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly IDistributedCache _distributedCache;
        private readonly ILogger<EmojiCatalogService> _logger;

        public EmojiCatalogService(
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache,
            IDistributedCache distributedCache,
            ILogger<EmojiCatalogService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _memoryCache = memoryCache;
            _distributedCache = distributedCache;
            _logger = logger;
        }

        public async Task<IReadOnlyList<string>> GetEmojiCatalogAsync(CancellationToken cancellationToken = default)
        {
            // L1: process-local memory cache
            if (_memoryCache.TryGetValue(CacheKey, out IReadOnlyList<string>? cached) && cached != null)
            {
                return cached;
            }

            // L2: distributed SQL cache
            try
            {
                var bytes = await _distributedCache.GetAsync(CacheKey, cancellationToken);
                if (bytes != null)
                {
                    var distributed = JsonSerializer.Deserialize<List<string>>(bytes);
                    if (distributed != null && distributed.Count > 0)
                    {
                        _memoryCache.Set(CacheKey, (IReadOnlyList<string>)distributed, TimeSpan.FromHours(12));
                        return distributed;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read emoji catalog from distributed cache");
            }

            // L3: external sources
            try
            {
                var client = _httpClientFactory.CreateClient();
                var emojis = await LoadFromSourcesAsync(client, cancellationToken);

                if (emojis == null || emojis.Count == 0)
                {
                    return SetFallback();
                }

                _memoryCache.Set(CacheKey, (IReadOnlyList<string>)emojis, TimeSpan.FromHours(12));

                try
                {
                    var serialized = JsonSerializer.SerializeToUtf8Bytes(emojis);
                    await _distributedCache.SetAsync(
                        CacheKey,
                        serialized,
                        new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(12) },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist emoji catalog to distributed cache");
                }

                return emojis;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load external emoji catalog. Using fallback.");
                return SetFallback();
            }
        }

        private async Task<List<string>?> LoadFromSourcesAsync(HttpClient client, CancellationToken cancellationToken)
        {
            foreach (var sourceUrl in SourceUrls)
            {
                try
                {
                    using var response = await client.GetAsync(sourceUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                    var parsed = ParseEmojisFromJson(raw);
                    if (parsed.Count > 0)
                    {
                        _logger.LogInformation("Emoji catalog loaded from {SourceUrl}. Count={Count}", sourceUrl, parsed.Count);
                        return parsed;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load emoji catalog from {SourceUrl}", sourceUrl);
                }
            }

            return null;
        }

        private static List<string> ParseEmojisFromJson(string raw)
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            var list = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var emoji = ReadEmojiValue(element);
                if (!string.IsNullOrWhiteSpace(emoji))
                {
                    list.Add(emoji.Trim());
                }
            }

            return list
                .Distinct(StringComparer.Ordinal)
                .Take(1500)
                .ToList();
        }

        private static string? ReadEmojiValue(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (element.TryGetProperty("emoji", out var emojiProp) && emojiProp.ValueKind == JsonValueKind.String)
            {
                return emojiProp.GetString();
            }

            if (element.TryGetProperty("char", out var charProp) && charProp.ValueKind == JsonValueKind.String)
            {
                return charProp.GetString();
            }

            return null;
        }

        private IReadOnlyList<string> SetFallback()
        {
            var fallback = FallbackEmojis
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            _memoryCache.Set(CacheKey, (IReadOnlyList<string>)fallback, TimeSpan.FromHours(12));
            return fallback;
        }
    }
}
