using BotManager.Backend.Entities.Entities;
using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.Entities
{
    public class BotManagerDbContext : DbContext
    {
        public BotManagerDbContext(DbContextOptions<BotManagerDbContext> options) : base(options)
        {
        }

        public DbSet<Bot> Bots { get; set; }
        public DbSet<BotConfiguration> BotConfigurations { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<BotHistory> BotHistories { get; set; }
        public DbSet<BotCommand> BotCommands { get; set; }
        public DbSet<CommandUsageLog> CommandUsageLogs { get; set; }
        public DbSet<LoginAuditLog> LoginAuditLogs { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
        public DbSet<SystemConfig> SystemConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure 1-to-1 relationship between Bot and BotConfiguration
            modelBuilder.Entity<Bot>()
                .HasOne(b => b.Configuration)
                .WithOne(c => c.Bot)
                .HasForeignKey<BotConfiguration>(c => c.BotId)
                .OnDelete(DeleteBehavior.Cascade);

            // Bot -> BotHistories (1-to-many)
            modelBuilder.Entity<BotHistory>()
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
        }
    }
}
