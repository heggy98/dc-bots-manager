using Microsoft.EntityFrameworkCore;
using BotManager.Backend.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Bots.Services.Implementations;
using BotManager.Backend.Shared.Services;
using BotManager.Backend.Shared.Models;
using BotManager.Backend.Services.Interfaces;
using BotManager.Backend.Services.Implementation;
using BotManager.Backend.API.Services;
using BotManager.Backend.API.Hubs;
using BotManager.Backend.API.BotPlugins;
using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Handlers;
using BotManager.Backend.API.BotPlugins.DiscordBoardPlugin.Services;
using BotManager.Backend.Entities.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using System.Collections.ObjectModel;
using System.Data;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.RateLimiting;

// Utility mode: `dotnet run -- --hash-password <password>` prints a BCrypt hash for AdminCredentials:PasswordHash.
if (args.Length == 2 && args[0] == "--hash-password")
{
    Console.WriteLine(new PasswordHasherService().HashPassword(args[1]));
    return;
}

// Surface Serilog sink failures (e.g. SQL insert errors) instead of dropping them silently.
Serilog.Debugging.SelfLog.Enable(msg => Console.Error.WriteLine(msg));

// Configure Serilog early
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

WebApplication? app = null;
var cleanupTrigger = 0;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog: read from config and add MSSqlServer sink for SystemLogs table
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    // The SystemLogs table schema is owned by EF migrations; lengths match the entity so the
    // sink truncates long messages/stack traces instead of failing the whole batch.
    var columnOptions = new ColumnOptions();
    columnOptions.Store.Remove(StandardColumn.Properties);
    columnOptions.Store.Remove(StandardColumn.MessageTemplate);
    // SqlBulkCopy column mapping is case-sensitive: the EF column is "Timestamp" (sink default "TimeStamp").
    columnOptions.TimeStamp.ColumnName = "Timestamp";
    columnOptions.TimeStamp.ConvertToUtc = true;
    columnOptions.Message.DataLength = 4000;
    columnOptions.Exception.DataLength = 4000;
    columnOptions.Level.DataLength = 50;
    columnOptions.AdditionalColumns = new Collection<SqlColumn>
    {
        new SqlColumn { ColumnName = "Category", PropertyName = "SourceContext", DataType = SqlDbType.NVarChar, DataLength = 500, AllowNull = true },
        new SqlColumn { ColumnName = "BotId", DataType = SqlDbType.Int, AllowNull = true }
    };

    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        // Persist warnings/errors plus bot-scoped events (shown in the bot detail); skip general chatter.
        .WriteTo.Logger(sql => sql
            .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Warning || e.Properties.ContainsKey("BotId"))
            .WriteTo.MSSqlServer(
                connectionString: connectionString,
                sinkOptions: new MSSqlServerSinkOptions
                {
                    TableName = "SystemLogs",
                    AutoCreateSqlTable = false
                },
                columnOptions: columnOptions
            )));

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddHttpClient();
    builder.Services.AddMemoryCache();
    // Persist the Data Protection key ring (it encrypts bot tokens) so tokens survive restarts and redeploys.
    var dataProtection = builder.Services.AddDataProtection()
        .SetApplicationName("BotManager")
        .PersistKeysToDbContext<BotManagerDbContext>();
    var dataProtectionCertPath = builder.Configuration["DataProtection:CertificatePath"];
    if (!string.IsNullOrWhiteSpace(dataProtectionCertPath))
    {
        dataProtection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(
            dataProtectionCertPath, builder.Configuration["DataProtection:CertificatePassword"]));
    }
    else if (OperatingSystem.IsWindows())
    {
        dataProtection.ProtectKeysWithDpapi();
    }

    // SignalR for real-time bot event delivery
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    });

    // SQL Server distributed cache (L2 cache surviving restarts)
    builder.Services.AddDistributedSqlServerCache(options =>
    {
        options.ConnectionString = connectionString;
        options.SchemaName = "dbo";
        options.TableName = "BotDistributedCache";
        options.DefaultSlidingExpiration = TimeSpan.FromMinutes(30);
    });

    builder.Services.AddDbContext<BotManagerDbContext>(options =>
        options.UseSqlServer(connectionString));

    // Register services
    builder.Services.AddSingleton<IBruteforceProtectionService, BruteforceProtectionService>();
    builder.Services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
    builder.Services.AddSingleton<AdminCredentialsService>();
    builder.Services.AddSingleton<JwtTokenService>();
    builder.Services.AddHostedService<LogRetentionService>();
    builder.Services.AddSingleton<IDiscordBotService, DiscordBotRuntimeService>();
    builder.Services.AddSingleton<IBoardMessageLocator, BoardMessageLocator>();
    builder.Services.AddSingleton<IPluginRegistry, PluginRegistry>();
    builder.Services.AddScoped<IPluginContextFactory, PluginContextFactory>();
    builder.Services.AddScoped<ITeamsDataService, DbTeamsDataService>();
    builder.Services.AddScoped<IGroupDataService, DbTeamsDataService>();
    builder.Services.AddScoped<IGroupBoardDataService, TeamBoardGroupDataService>();
    builder.Services.AddScoped<IBotDataService, DbBotDataService>();
    builder.Services.AddScoped<CommandManagementService>();
    builder.Services.AddScoped<IBoardCommandAuditService, BoardCommandAuditService>();
    builder.Services.AddScoped<TeamsCommandHandler>();
    builder.Services.AddScoped<ReactionsCommandHandler>();
    builder.Services.AddScoped<BoardCommandService>();
    builder.Services.AddScoped<IBoardCommandService>(sp => sp.GetRequiredService<BoardCommandService>());
    builder.Services.AddScoped<IDiscordCommandProvider>(sp => sp.GetRequiredService<BoardCommandService>());
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<BotManagementService>();
    builder.Services.AddScoped<ISystemConfigService, SystemConfigService>();
    builder.Services.AddScoped<IUserIdentityResolver, UserIdentityResolver>();
    builder.Services.AddScoped<DiscordBotIdentityService>();
    builder.Services.AddScoped<IBotTokenSecurityService, BotTokenSecurityService>();
    builder.Services.AddSingleton<IEmojiCatalogService, EmojiCatalogService>();
    builder.Services.AddSingleton<IBotNotificationService, BotNotificationService>();

    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (corsOrigins == null || corsOrigins.Length == 0)
    {
        corsOrigins = ["http://localhost:4200", "https://localhost:4200"];
    }

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAngular", policy =>
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Behind a reverse proxy, trust X-Forwarded-For only from explicitly configured proxies,
    // otherwise all clients would share the proxy IP (and lockouts/rate limits would hit everyone).
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(proxy, out var proxyAddress))
            {
                options.KnownProxies.Add(proxyAddress);
            }
        }
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy(RateLimitPolicies.Public, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });

    builder.Services.AddExceptionHandler(options => { });
    builder.Services.AddProblemDetails();

    // Validates JWT secret length at startup.
    var signingKey = JwtTokenService.CreateSigningKey(builder.Configuration);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
                ValidAudience = builder.Configuration["JwtSettings:Audience"],
                IssuerSigningKey = signingKey,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            // SignalR WebSocket connections pass the JWT as a query-string parameter.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

    // Every [Authorize] endpoint (and the SignalR hub) requires the Admin role, not just any valid token.
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireRole(AuthRoles.Admin)
            .Build();
    });

    app = builder.Build();

    // Fail fast on missing/invalid admin credentials instead of at the first login.
    app.Services.GetRequiredService<AdminCredentialsService>();

    app.Lifetime.ApplicationStopping.Register(() =>
    {
        if (Interlocked.Exchange(ref cleanupTrigger, 1) == 1)
        {
            return;
        }

        try
        {
            EmergencyShutdownCleanupAsync(
                app.Services,
                "Application shutdown",
                "Host is stopping.").GetAwaiter().GetResult();
        }
        catch (Exception cleanupEx)
        {
            Log.Error(cleanupEx, "Emergency shutdown cleanup failed during ApplicationStopping");
        }
    });

    // Ensure database schema is up to date on startup.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();
        db.Database.Migrate();

        await ProtectLegacyBotTokensAsync(db, scope.ServiceProvider.GetRequiredService<IBotTokenSecurityService>());
    }

    app.UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    else
    {
        app.UseExceptionHandler();
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseCors("AllowAngular");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHub<BotEventsHub>("/hubs/bot-events");

    // Ensure distributed cache table exists.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<BotManagerDbContext>();
        db.Database.ExecuteSqlRaw(@"
            IF OBJECT_ID(N'dbo.BotDistributedCache', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[BotDistributedCache] (
                    [Id]                         NVARCHAR(449)    NOT NULL,
                    [Value]                      VARBINARY(MAX)   NOT NULL,
                    [ExpiresAtTime]              DATETIMEOFFSET   NOT NULL,
                    [SlidingExpirationInSeconds] BIGINT           NULL,
                    [AbsoluteExpiration]         DATETIMEOFFSET   NULL,
                    CONSTRAINT [pk_BotDistributedCache] PRIMARY KEY ([Id])
                );
                CREATE NONCLUSTERED INDEX [Index_ExpiresAtTime]
                    ON [dbo].[BotDistributedCache] ([ExpiresAtTime]);
            END");
    }

    Log.Information("BotManager API started");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");

    if (app != null && Interlocked.Exchange(ref cleanupTrigger, 1) == 0)
    {
        try
        {
            await EmergencyShutdownCleanupAsync(
                app.Services,
                "Application crash",
                ex.ToString());
        }
        catch (Exception cleanupEx)
        {
            Log.Error(cleanupEx, "Emergency shutdown cleanup failed after crash");
        }
    }
}
finally
{
    Log.CloseAndFlush();
}

static async Task ProtectLegacyBotTokensAsync(BotManagerDbContext db, IBotTokenSecurityService tokenSecurity)
{
    // One-off upgrade: bot tokens stored before encryption was introduced are re-protected.
    var bots = await db.Bots.Where(b => !b.BotToken.StartsWith("v1:")).ToListAsync();
    var converted = 0;
    foreach (var bot in bots)
    {
        if (tokenSecurity.TryProtectLegacyToken(bot.BotToken, out var protectedToken))
        {
            bot.BotToken = protectedToken;
            converted++;
        }
    }

    if (converted > 0)
    {
        await db.SaveChangesAsync();
        Log.Warning("Re-protected {Count} legacy plain-text bot tokens", converted);
    }
}

static async Task EmergencyShutdownCleanupAsync(IServiceProvider services, string reason, string? details)
{
    Log.Warning("Starting emergency cleanup. Reason={Reason}", reason);

    using var scope = services.CreateScope();
    var scopedServices = scope.ServiceProvider;

    var runtimeService = scopedServices.GetService<IDiscordBotService>();
    if (runtimeService != null)
    {
        try
        {
            await runtimeService.StopAllAsync();
        }
        catch (Exception runtimeStopEx)
        {
            Log.Warning(runtimeStopEx, "Failed to stop Discord runtime during emergency cleanup");
        }
    }

    try
    {
        var db = scopedServices.GetRequiredService<BotManagerDbContext>();
        var stopAt = DateTime.UtcNow;

        var activeBots = await db.Bots
            .Where(b => b.Status != BotStatus.Offline)
            .ToListAsync();

        foreach (var bot in activeBots)
        {
            bot.Status = BotStatus.Offline;
            bot.LastStoppedAt = stopAt;
        }

        var openHistories = await db.BotRunHistories
            .Where(h => h.StoppedAt == null)
            .ToListAsync();

        var historyReason = reason.Length > 500 ? reason.Substring(0, 500) : reason;
        var historyDetails = details;
        if (!string.IsNullOrEmpty(historyDetails) && historyDetails.Length > 2000)
        {
            historyDetails = historyDetails.Substring(0, 2000);
        }

        foreach (var history in openHistories)
        {
            history.StoppedAt = stopAt;
            history.DurationSeconds = (long)(stopAt - history.StartedAt).TotalSeconds;
            history.StopReason = historyReason;
            history.ErrorDetails = historyDetails;
        }

        await db.SaveChangesAsync();

        Log.Warning(
            "Emergency cleanup completed. Marked {BotCount} bots offline and closed {HistoryCount} open histories.",
            activeBots.Count,
            openHistories.Count);
    }
    catch (Exception dbEx)
    {
        Log.Error(dbEx, "Failed to persist offline state during emergency cleanup");
    }
}
