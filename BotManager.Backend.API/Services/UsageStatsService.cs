using BotManager.Backend.API.Models;
using BotManager.Backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Computes command usage and uptime statistics. Aggregation happens in SQL (GROUP BY day / command);
    /// only the per-bot run intervals overlapping the window are loaded to compute uptime.
    /// </summary>
    public class UsageStatsService
    {
        public const int MinDays = 1;
        public const int MaxDays = 90;
        public const int DefaultDays = 30;
        public const int TopCommandsLimit = 10;

        private const string ReactionAssign = "reaction-assign";
        private const string ReactionUnassign = "reaction-unassign";

        private readonly BotManagerDbContext _db;
        private readonly Func<DateTime> _utcNow;

        public UsageStatsService(BotManagerDbContext db, Func<DateTime>? utcNow = null)
        {
            _db = db;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Clamps a requested window length to the supported range.
        /// </summary>
        public static int ClampDays(int? days) => Math.Clamp(days ?? DefaultDays, MinDays, MaxDays);

        /// <summary>
        /// Statistics across all bots owned by <paramref name="ownerUserId"/>.
        /// </summary>
        public async Task<UsageStatsDto> GetOwnerStatsAsync(string ownerUserId, int? days, CancellationToken ct = default)
        {
            var botIds = await _db.Bots.AsNoTracking()
                .Where(b => b.OwnerUserId == ownerUserId)
                .Select(b => b.BotId)
                .ToListAsync(ct);

            var stats = await BuildAsync(botIds, days, ct);
            return stats;
        }

        /// <summary>
        /// Statistics for a single bot, or null when the bot does not exist.
        /// </summary>
        public async Task<UsageStatsDto?> GetBotStatsAsync(int botId, int? days, CancellationToken ct = default)
        {
            var exists = await _db.Bots.AsNoTracking().AnyAsync(b => b.BotId == botId, ct);
            if (!exists)
            {
                return null;
            }

            var stats = await BuildAsync([botId], days, ct);
            stats.BotId = botId;
            return stats;
        }

        private async Task<UsageStatsDto> BuildAsync(List<int> botIds, int? requestedDays, CancellationToken ct)
        {
            var days = ClampDays(requestedDays);
            var now = _utcNow();
            var windowStart = now.Date.AddDays(-(days - 1));

            var result = new UsageStatsDto { Days = days, From = windowStart, To = now };

            if (botIds.Count == 0)
            {
                result.Daily = FillDays(windowStart, days, []);
                return result;
            }

            // Filter on ExecutedAt first (index), join to the bot's commands.
            var rows = from log in _db.CommandUsageLogs.AsNoTracking()
                       where log.ExecutedAt >= windowStart
                       join command in _db.BotCommands.AsNoTracking() on log.CommandId equals command.CommandId
                       where botIds.Contains(command.BotId)
                       select new { log.ExecutedAt, log.IsSuccess, command.CommandName, command.SubCommandName };

            var dailyRows = await rows
                .GroupBy(x => x.ExecutedAt.Date)
                .Select(g => new DailyAggregate(
                    g.Key,
                    g.Count(),
                    g.Count(x => !x.IsSuccess),
                    g.Count(x => x.IsSuccess && x.CommandName == ReactionAssign),
                    g.Count(x => x.IsSuccess && x.CommandName == ReactionUnassign)))
                .ToListAsync(ct);

            var topRows = await rows
                .GroupBy(x => new { x.CommandName, x.SubCommandName })
                .Select(g => new
                {
                    g.Key.CommandName,
                    g.Key.SubCommandName,
                    Count = g.Count(),
                    Errors = g.Count(x => !x.IsSuccess)
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.CommandName)
                .ThenBy(x => x.SubCommandName)
                .Take(TopCommandsLimit)
                .ToListAsync(ct);

            result.Daily = FillDays(windowStart, days, dailyRows);
            result.Totals = new UsageTotalsDto
            {
                Commands = result.Daily.Sum(d => d.Commands),
                Errors = result.Daily.Sum(d => d.Errors),
                Joins = result.Daily.Sum(d => d.Joins),
                Leaves = result.Daily.Sum(d => d.Leaves)
            };
            result.TopCommands = topRows
                .Select(r => new TopCommandDto
                {
                    Command = FormatCommandLabel(r.CommandName, r.SubCommandName),
                    CommandName = r.CommandName,
                    SubCommandName = r.SubCommandName,
                    Count = r.Count,
                    Errors = r.Errors
                })
                .ToList();
            result.Uptime = await LoadUptimeAsync(botIds, windowStart, now, ct);
            return result;
        }

        private async Task<List<BotUptimeDto>> LoadUptimeAsync(List<int> botIds, DateTime from, DateTime now, CancellationToken ct)
        {
            var bots = await _db.Bots.AsNoTracking()
                .Where(b => botIds.Contains(b.BotId))
                .OrderBy(b => b.Name)
                .Select(b => new { b.BotId, b.Name, b.Status })
                .ToListAsync(ct);

            // Only intervals overlapping [from, now]; open intervals (StoppedAt null) are still running.
            var intervals = await _db.BotRunHistories.AsNoTracking()
                .Where(h => botIds.Contains(h.BotId)
                    && h.StartedAt < now
                    && (h.StoppedAt == null || h.StoppedAt > from))
                .Select(h => new { h.BotId, h.StartedAt, h.StoppedAt })
                .ToListAsync(ct);

            var byBot = intervals
                .GroupBy(i => i.BotId)
                .ToDictionary(g => g.Key, g => g.Select(i => new RunInterval(i.StartedAt, i.StoppedAt)).ToList());

            var windowSeconds = (long)(now - from).TotalSeconds;
            return bots
                .Select(b =>
                {
                    var seconds = byBot.TryGetValue(b.BotId, out var list)
                        ? UptimeCalculator.ComputeUptimeSeconds(list, from, now)
                        : 0;
                    return new BotUptimeDto
                    {
                        BotId = b.BotId,
                        Name = b.Name,
                        Status = b.Status.ToString(),
                        UptimeSeconds = seconds,
                        WindowSeconds = windowSeconds,
                        UptimePercent = UptimeCalculator.ToPercent(seconds, windowSeconds)
                    };
                })
                .ToList();
        }

        /// <summary>
        /// Produces one row per day of the window (oldest first), zero-filling days without activity.
        /// </summary>
        public static List<DailyUsageDto> FillDays(DateTime from, int days, IReadOnlyCollection<DailyAggregate> aggregates)
        {
            var byDay = aggregates.ToDictionary(a => a.Day.Date);
            var list = new List<DailyUsageDto>(days);
            for (var i = 0; i < days; i++)
            {
                var day = from.Date.AddDays(i);
                byDay.TryGetValue(day, out var agg);
                list.Add(new DailyUsageDto
                {
                    Date = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    Commands = agg?.Commands ?? 0,
                    Errors = agg?.Errors ?? 0,
                    Joins = agg?.Joins ?? 0,
                    Leaves = agg?.Leaves ?? 0
                });
            }

            return list;
        }

        public static string FormatCommandLabel(string commandName, string? subCommandName)
        {
            if (commandName.StartsWith("reaction-", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{commandName}]";
            }

            return string.IsNullOrWhiteSpace(subCommandName) ? $"/{commandName}" : $"/{commandName} {subCommandName}";
        }

        public sealed record DailyAggregate(DateTime Day, int Commands, int Errors, int Joins, int Leaves);
    }

    /// <summary>A bot run interval; <see cref="Stop"/> is null while the run is still open.</summary>
    public readonly record struct RunInterval(DateTime Start, DateTime? Stop);

    /// <summary>
    /// Pure uptime math over run intervals.
    /// </summary>
    public static class UptimeCalculator
    {
        /// <summary>
        /// Total seconds covered by the intervals inside [windowStart, windowEnd]. Intervals are clipped to the
        /// window, open intervals count until <paramref name="windowEnd"/>, and overlapping intervals
        /// (e.g. a reconnect row opened before the previous row was closed) are merged so time is never counted twice.
        /// </summary>
        public static long ComputeUptimeSeconds(IEnumerable<RunInterval> intervals, DateTime windowStart, DateTime windowEnd)
        {
            if (windowEnd <= windowStart)
            {
                return 0;
            }

            var clipped = intervals
                .Select(i => (Start: i.Start < windowStart ? windowStart : i.Start,
                              End: (i.Stop ?? windowEnd) > windowEnd ? windowEnd : (i.Stop ?? windowEnd)))
                .Where(i => i.End > i.Start)
                .OrderBy(i => i.Start)
                .ToList();

            var total = TimeSpan.Zero;
            DateTime? currentStart = null;
            DateTime currentEnd = default;

            foreach (var (start, end) in clipped)
            {
                if (currentStart == null)
                {
                    currentStart = start;
                    currentEnd = end;
                }
                else if (start <= currentEnd)
                {
                    if (end > currentEnd) currentEnd = end;
                }
                else
                {
                    total += currentEnd - currentStart.Value;
                    currentStart = start;
                    currentEnd = end;
                }
            }

            if (currentStart != null)
            {
                total += currentEnd - currentStart.Value;
            }

            return (long)total.TotalSeconds;
        }

        /// <summary>
        /// Converts covered seconds to a percentage (0..100) of the window, rounded to one decimal.
        /// </summary>
        public static double ToPercent(long uptimeSeconds, long windowSeconds)
        {
            if (windowSeconds <= 0) return 0;
            var percent = uptimeSeconds * 100.0 / windowSeconds;
            return Math.Round(Math.Clamp(percent, 0, 100), 1);
        }
    }
}
