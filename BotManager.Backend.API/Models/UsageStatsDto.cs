namespace BotManager.Backend.API.Models
{
    /// <summary>
    /// Usage statistics over a window of whole UTC days ending today.
    /// </summary>
    public class UsageStatsDto
    {
        /// <summary>Number of days in the window (1..90).</summary>
        public int Days { get; set; }

        /// <summary>Window start (UTC midnight of the first day).</summary>
        public DateTime From { get; set; }

        /// <summary>Window end (the time the statistics were computed, UTC).</summary>
        public DateTime To { get; set; }

        /// <summary>Set for per-bot statistics.</summary>
        public int? BotId { get; set; }

        public UsageTotalsDto Totals { get; set; } = new();

        /// <summary>One row per UTC day of the window, oldest first; days without activity are zero-filled.</summary>
        public List<DailyUsageDto> Daily { get; set; } = [];

        /// <summary>Most used commands, descending by executions.</summary>
        public List<TopCommandDto> TopCommands { get; set; } = [];

        /// <summary>Uptime per bot over the window.</summary>
        public List<BotUptimeDto> Uptime { get; set; } = [];
    }

    public class UsageTotalsDto
    {
        public int Commands { get; set; }
        public int Errors { get; set; }

        /// <summary>Successful reaction-based team joins (reaction-assign).</summary>
        public int Joins { get; set; }

        /// <summary>Successful reaction-based team leaves (reaction-unassign).</summary>
        public int Leaves { get; set; }
    }

    public class DailyUsageDto
    {
        /// <summary>UTC day, formatted yyyy-MM-dd.</summary>
        public string Date { get; set; } = string.Empty;
        public int Commands { get; set; }
        public int Errors { get; set; }
        public int Joins { get; set; }
        public int Leaves { get; set; }
    }

    public class TopCommandDto
    {
        /// <summary>Display label, e.g. "/board add-team" or "[reaction-assign]".</summary>
        public string Command { get; set; } = string.Empty;
        public string CommandName { get; set; } = string.Empty;
        public string? SubCommandName { get; set; }
        public int Count { get; set; }
        public int Errors { get; set; }
    }

    public class BotUptimeDto
    {
        public int BotId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long UptimeSeconds { get; set; }
        public long WindowSeconds { get; set; }

        /// <summary>Uptime share of the window, 0..100, one decimal.</summary>
        public double UptimePercent { get; set; }
    }
}
