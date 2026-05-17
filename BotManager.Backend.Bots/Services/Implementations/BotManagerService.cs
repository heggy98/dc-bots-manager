namespace BotManager.Backend.Bots.Services.Implementations;

using BotManager.Backend.Bots.Services.Contracts;
using BotManager.Backend.Entities;
using BotManager.Backend.Entities.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Diagnostics;
using System.IO;

/// <summary>
/// Starts and stops a standalone Discord bot process configured via app settings.
/// </summary>
public class BotManagerService : IBotManagerService
{
    private readonly ILogger<BotManagerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly BotManagerDbContext _db;
    private Process? _botProcess;

    /// <summary>
    /// Creates a new process-based bot manager service.
    /// </summary>
    public BotManagerService(ILogger<BotManagerService> logger, IConfiguration configuration, BotManagerDbContext db)
    {
        _logger = logger;
        _configuration = configuration;
        _db = db;
    }

    /// <summary>
    /// Starts the configured Discord bot process if it is not already running.
    /// </summary>
    public void StartBot()
    {
        if (_botProcess == null || _botProcess.HasExited)
        {
            var botPath = _configuration["DiscordBot:ExecutablePath"];

            if (string.IsNullOrEmpty(botPath) || !File.Exists(botPath))
            {
                _logger.LogError("Cesta ke spustitelnemu souboru bota neni platna: '{BotPath}'. Zkontrolujte konfiguraci 'DiscordBot:ExecutablePath'.", botPath);
                return;
            }

            var workingDirectory = Path.GetDirectoryName(botPath) ?? string.Empty;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = botPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _botProcess = new Process();
            _botProcess.StartInfo = startInfo;
            _botProcess.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    _logger.LogInformation("[BOT Output]: {Output}", args.Data);
                }
            };
            _botProcess.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    _logger.LogError("[BOT Error]: {Error}", args.Data);
                }
            };

            try
            {
                _botProcess.Start();
                _botProcess.BeginOutputReadLine();
                _botProcess.BeginErrorReadLine();
                _logger.LogInformation("Discord bot byl spuštěn.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chyba pri spousteni Discord bota.");
                _botProcess = null;
            }
        }
        else
        {
            _logger.LogInformation("Discord bot již běží.");
        }
    }

    /// <summary>
    /// Stops the running Discord bot process if present.
    /// </summary>
    public void StopBot()
    {
        if (_botProcess != null && !_botProcess.HasExited)
        {
            try
            {
                _botProcess.Kill(); // Ukončí proces bota
                _botProcess.WaitForExit();
                _logger.LogInformation("Discord bot byl ukončen.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chyba pri ukoncovani Discord bota.");
            }
            finally
            {
                _botProcess.Dispose();
                _botProcess = null;
            }
        }
        else
        {
            _logger.LogInformation("Discord bot neběží.");
        }

        try
        {
            MarkAllActiveBotsOfflineAsync("Manual process stop").GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chyba pri nastavovani stavu bota na Offline po zastaveni procesu.");
        }
    }

    /// <summary>
    /// Marks all non-offline bots offline and closes any open run histories.
    /// </summary>
    private async Task MarkAllActiveBotsOfflineAsync(string reason)
    {
        var stopAt = DateTime.UtcNow;
        var activeBots = await _db.Bots
            .Where(bot => bot.Status != BotStatus.Offline)
            .ToListAsync();

        foreach (var bot in activeBots)
        {
            bot.Status = BotStatus.Offline;
            bot.LastStoppedAt = stopAt;
        }

        var openHistories = await _db.BotRunHistories
            .Where(history => history.StoppedAt == null)
            .ToListAsync();

        foreach (var history in openHistories)
        {
            history.StoppedAt = stopAt;
            history.DurationSeconds = (long)(stopAt - history.StartedAt).TotalSeconds;
            history.StopReason = reason.Length > 500 ? reason[..500] : reason;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Marked {BotCount} active bots offline and closed {HistoryCount} open histories after process stop.",
            activeBots.Count,
            openHistories.Count);
    }
}
