using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.Entities
{
    /// <summary>
    /// Entity Framework database context for bot manager domain entities.
    /// </summary>
    public class BotManagerDbContext : DbContext
    {
        /// <summary>
        /// Creates a new database context with configured options.
        /// </summary>
        public BotManagerDbContext(DbContextOptions<BotManagerDbContext> options) : base(options)
        {
        }

        public DbSet<Bot> Bots { get; set; }
        public DbSet<BotConfiguration> BotConfigurations { get; set; }
        public DbSet<BoardGlobalConfig> BoardGlobalConfigs { get; set; }
        public DbSet<BoardConfiguration> BoardConfigurations { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<BotRunHistory> BotRunHistories  { get; set; }
        public DbSet<BotCommand> BotCommands { get; set; }
        public DbSet<CommandUsageLog> CommandUsageLogs { get; set; }
        public DbSet<LoginAuditLog> LoginAuditLogs { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
        public DbSet<SystemConfig> SystemConfigs { get; set; }

        /// <summary>
        /// Configures entity relationships and seed data.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure 1-to-1 relationship between Bot and BotConfiguration
            modelBuilder.Entity<Bot>()
                .HasOne(b => b.Configuration)
                .WithOne(c => c.Bot)
                .HasForeignKey<BotConfiguration>(c => c.BotId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BotConfiguration>()
                .HasOne(c => c.ActiveBoardConfiguration)
                .WithMany()
                .HasForeignKey(c => c.ActiveBoardConfigurationId)
                .OnDelete(DeleteBehavior.NoAction);

            // Bot -> BoardConfigurations (1-to-many)
            modelBuilder.Entity<BoardConfiguration>()
                .HasOne(c => c.Bot)
                .WithMany(b => b.BoardConfigurations)
                .HasForeignKey(c => c.BotId)
                .OnDelete(DeleteBehavior.Cascade);

            // BoardConfiguration -> Teams (1-to-many)
            modelBuilder.Entity<Team>()
                .HasOne(t => t.BoardConfiguration)
                .WithMany(c => c.Teams)
                .HasForeignKey(t => t.BoardConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Bot -> BotHistories (1-to-many)
            modelBuilder.Entity<BotRunHistory>()
                .HasOne(h => h.Bot)
                .WithMany(b => b.Histories)
                .HasForeignKey(h => h.BotId)
                .OnDelete(DeleteBehavior.Cascade);

            // Bot -> BotCommands (1-to-many)
            modelBuilder.Entity<BotCommand>()
                .HasOne(c => c.Bot)
                .WithMany(b => b.Commands)
                .HasForeignKey(c => c.BotId)
                .OnDelete(DeleteBehavior.Cascade);

            // BotCommand -> CommandUsageLogs (1-to-many)
            modelBuilder.Entity<CommandUsageLog>()
                .HasOne(u => u.BotCommand)
                .WithMany(c => c.UsageLogs)
                .HasForeignKey(u => u.CommandId)
                .OnDelete(DeleteBehavior.Cascade);

            // Seed default system config
            modelBuilder.Entity<SystemConfig>().HasData(
                new SystemConfig { Id = 1, Key = "Bruteforce.MaxAttempts", Value = "5", Description = "Maximum failed login attempts before lockout" },
                new SystemConfig { Id = 2, Key = "Bruteforce.LockoutMinutes", Value = "5", Description = "Minutes to lock out after too many failures" }
            );

            modelBuilder.Entity<BoardGlobalConfig>().HasData(
                new BoardGlobalConfig
                {
                    Id = 1,
                    DefaultBoardTitle = "📋 Seznam všech skupin",
                    DefaultBoardDescription = "Celkem registrovaných skupin: {count}",
                    DefaultBoardDetailSubtitleLabel = "Podtitul",
                    DefaultBoardDetailContactLabel = "Kontakt"
                }
            );
        }
    }
}
