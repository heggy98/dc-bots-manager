using Microsoft.EntityFrameworkCore;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Supported database providers, selected with <c>Database:Provider</c>.
    /// </summary>
    public enum DatabaseProvider
    {
        /// <summary>Microsoft SQL Server / LocalDB / Azure SQL (x86-64 only for the Linux container).</summary>
        SqlServer,

        /// <summary>PostgreSQL (runs on ARM64, e.g. Oracle Cloud Always Free Ampere VMs).</summary>
        PostgreSql
    }

    /// <summary>
    /// Configures the EF Core provider and the matching migrations assembly.
    /// </summary>
    public static class DatabaseSetup
    {
        /// <summary>Assembly holding the PostgreSQL migrations.</summary>
        public const string PostgreSqlMigrationsAssembly = "BotManager.Backend.Entities.PostgreSql";

        /// <summary>
        /// Reads <c>Database:Provider</c> (default SqlServer; accepts "Postgres"/"PostgreSQL"/"Npgsql").
        /// </summary>
        public static DatabaseProvider GetProvider(IConfiguration configuration)
        {
            var value = configuration["Database:Provider"];
            if (string.IsNullOrWhiteSpace(value) || value.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                || value.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseProvider.SqlServer;
            }

            if (value.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) || value.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseProvider.PostgreSql;
            }

            throw new InvalidOperationException($"Unsupported Database:Provider '{value}'. Use SqlServer or PostgreSql.");
        }

        /// <summary>
        /// Applies the provider to a DbContext options builder.
        /// </summary>
        public static void Configure(DbContextOptionsBuilder options, DatabaseProvider provider, string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
            }

            switch (provider)
            {
                case DatabaseProvider.PostgreSql:
                    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgreSqlMigrationsAssembly));
                    break;
                default:
                    options.UseSqlServer(connectionString);
                    break;
            }
        }

        /// <summary>
        /// Must run before any Npgsql usage: keeps DateTime mapped to "timestamp without time zone",
        /// matching SQL Server's datetime2 semantics (values are stored as UTC by the application).
        /// </summary>
        public static void ConfigureNpgsqlCompatibility()
            => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }
}
