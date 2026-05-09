using Microsoft.EntityFrameworkCore;
using BotManager.Backend.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using BotManager.Backend.Services.Interfaces;
using BotManager.Backend.Services.Implementation;
using BotManager.Api.Services;
using BotManager.Api.BotPlugins;
using Serilog;
using Serilog.Sinks.MSSqlServer;
using System.Collections.ObjectModel;
using System.Data;

// Configure Serilog early
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

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

    builder.Services.AddDbContext<BotManagerDbContext>(options =>
        options.UseSqlServer(connectionString));

    // Register services
    builder.Services.AddSingleton<IBruteforceProtectionService, BruteforceProtectionService>();
    builder.Services.AddSingleton<IDiscordBotService, DiscordBotAllianceService>();
    builder.Services.AddSingleton<PluginRegistry>();
    builder.Services.AddScoped<FileTeamsDataService>();
    builder.Services.AddScoped<FileBotDataService>();
    builder.Services.AddScoped<ITeamsDataService, DbTeamsDataService>();
    builder.Services.AddScoped<IBotDataService, DbBotDataService>();
    builder.Services.AddScoped<CommandManagementService>();
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<BotManagementService>();
    builder.Services.AddScoped<SystemConfigService>();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAngular", policy =>
        {
            policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
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
        });

    builder.Services.AddAuthorization();

    var app = builder.Build();

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

    Log.Information("BotManager API started");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
