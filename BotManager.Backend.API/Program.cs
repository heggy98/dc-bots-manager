using Microsoft.EntityFrameworkCore;
using BotManager.Backend.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
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
using Serilog;
using Serilog.Sinks.MSSqlServer;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Threading;

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

    var columnOptions = new ColumnOptions();
    columnOptions.Store.Remove(StandardColumn.Properties);
    columnOptions.Store.Remove(StandardColumn.MessageTemplate);
    columnOptions.AdditionalColumns = new Collection<SqlColumn>
    {
        new SqlColumn { ColumnName = "Category", DataType = SqlDbType.NVarChar, DataLength = 500 }
    };

    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console()
        .WriteTo.MSSqlServer(
            connectionString: connectionString,
            sinkOptions: new MSSqlServerSinkOptions
            {
                TableName = "SystemLogs",
                AutoCreateSqlTable = true
            },
            columnOptions: columnOptions
        ));

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddHttpClient();
    builder.Services.AddMemoryCache();
    builder.Services.AddDataProtection();

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

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAngular", policy =>
        {
            policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    var jwtSecret = builder.Configuration["JwtSettings:Secret"];
    if (string.IsNullOrEmpty(jwtSecret)) throw new InvalidOperationException("JwtSettings:Secret is missing");

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
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
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

    builder.Services.AddAuthorization();

    app = builder.Build();

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
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();
    app.UseCors("AllowAngular");
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
            await runtimeService.StopAsync();
        }
        catch (Exception runtimeStopEx)
        {
            Log.Warning(runtimeStopEx, "Failed to stop Discord runtime during emergency cleanup");
        }
    }

    try
    {
        var configuration = scopedServices.GetRequiredService<IConfiguration>();
        var botExecutablePath = configuration["DiscordBot:ExecutablePath"];

        if (!string.IsNullOrWhiteSpace(botExecutablePath))
        {
            var processName = Path.GetFileNameWithoutExtension(botExecutablePath);
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var currentProcessId = Environment.ProcessId;
                var processes = Process.GetProcessesByName(processName)
                    .Where(p => p.Id != currentProcessId)
                    .ToList();

                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                        Log.Warning("Killed bot process {ProcessName} (PID {Pid}) during emergency cleanup", process.ProcessName, process.Id);
                    }
                    catch (Exception killEx)
                    {
                        Log.Warning(killEx, "Failed to kill process {ProcessName} (PID {Pid}) during emergency cleanup", process.ProcessName, process.Id);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
    }
    catch (Exception processSweepEx)
    {
        Log.Warning(processSweepEx, "Failed while sweeping external bot processes during emergency cleanup");
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
