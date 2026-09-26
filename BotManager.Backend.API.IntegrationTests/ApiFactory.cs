using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Testcontainers.MsSql;
using Xunit;

namespace BotManager.Backend.API.IntegrationTests;

/// <summary>
/// Starts SQL Server in a container and hosts the real API against it.
/// Configuration is passed via environment variables because Program.cs reads some settings
/// before the host is built.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "Integration-Test-Password-1";
    public const int LegacyBotId = 4242;
    public const string LegacyPlainToken = "legacy-plain-token";

    /// <summary>Header used by tests to choose the client IP (bruteforce/rate limits are per IP).</summary>
    public const string TestIpHeader = "X-Test-Client-IP";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        ConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "BotManagerTests",
            MultipleActiveResultSets = true
        }.ConnectionString;

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);
        Environment.SetEnvironmentVariable("JwtSettings__Secret", "integration-test-secret-0123456789abcdef-0123456789");
        Environment.SetEnvironmentVariable("JwtSettings__Issuer", "BotManager");
        Environment.SetEnvironmentVariable("JwtSettings__Audience", "BotManager");
        Environment.SetEnvironmentVariable("AdminCredentials__Email", AdminEmail);
        Environment.SetEnvironmentVariable("AdminCredentials__PasswordHash",
            new BotManager.Backend.Services.Implementation.PasswordHasherService().HashPassword(AdminPassword));
        Environment.SetEnvironmentVariable("Auth__CookieSecure", "false");
        Environment.SetEnvironmentVariable("Bots__AutoStartEnabled", "false");
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__PublicPermitLimit", "1000");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        // Pre-create the schema and a bot with a legacy plain-text token, to verify the startup upgrade.
        var options = new DbContextOptionsBuilder<BotManagerDbContext>().UseSqlServer(ConnectionString).Options;
        await using (var db = new BotManagerDbContext(options))
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync(
                "SET IDENTITY_INSERT Bots ON; INSERT INTO Bots (BotId, Name, BotToken, OwnerUserId, IsPublic, AutoStart, Status) " +
                "VALUES ({0}, 'legacy', {1}, {2}, 1, 0, 0); SET IDENTITY_INSERT Bots OFF;",
                LegacyBotId, LegacyPlainToken, AdminEmail);
        }

        // Force host start.
        _ = Server;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    public BotManagerDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<BotManagerDbContext>().UseSqlServer(ConnectionString).Options);

    /// <summary>
    /// Creates a cookie-handling client that appears to come from the given IP.
    /// </summary>
    public HttpClient CreateClientFrom(string ip)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add(TestIpHeader, ip);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, TestClientIpStartupFilter>());
    }

    private sealed class TestClientIpStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next)
            => app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (IPAddress.TryParse(context.Request.Headers[TestIpHeader], out var ip))
                    {
                        context.Connection.RemoteIpAddress = ip;
                    }
                    await nextMiddleware();
                });
                next(app);
            };
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
