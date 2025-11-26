namespace dcbotfrontend.Server.Services;
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class BotManager
{
    private readonly ILogger<BotManager> _logger;
    private readonly IConfiguration _configuration;
    private Process _botProcess;

    public BotManager(ILogger<BotManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public void StartBot()
    {
        if (_botProcess == null || _botProcess.HasExited)
        {
            string botPath = _configuration["DiscordBot:ExecutablePath"];
            string workingDirectory = Path.GetDirectoryName(botPath);

            if (string.IsNullOrEmpty(botPath) || !File.Exists(botPath))
            {
                _logger.LogError($"Cesta ke spustitelnému souboru bota není platná: '{botPath}'. Zkontrolujte konfiguraci 'DiscordBot:ExecutablePath'.");
                return;
            }

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
                    _logger.LogInformation($"[BOT Output]: {args.Data}");
                }
            };
            _botProcess.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    _logger.LogError($"[BOT Error]: {args.Data}");
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
                _logger.LogError($"Chyba při spouštění Discord bota: {ex.Message}");
                _botProcess = null;
            }
        }
        else
        {
            _logger.LogInformation("Discord bot již běží.");
        }
    }

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
                _logger.LogError($"Chyba při ukončování Discord bota: {ex.Message}");
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
    }
}
