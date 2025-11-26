using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace discord_bot_aliance
{
    public class Logger : ILogger
    {
        public static Func<string, Task> OnCriticalError;

        public Logger()
        {

        }
        public enum LogLevel
        {
            Info,
            Warning,
            Error,
            Debug,
            DiscordClientLog,
            Success
        }

        public Task Consol(string message, LogLevel level = LogLevel.Success)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var fullMessage = $"[{level}] {timestamp} {message}";

            switch (level)
            {
                case LogLevel.Info:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogLevel.Warning:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case LogLevel.Error:
                    if (OnCriticalError != null)
                    {
                        _ = Task.Run(async () => {
                            try
                            {
                                await OnCriticalError(message);
                            }
                            catch (Exception ex)
                            {
                                // Alternativní logování, pokud selže odeslání Discord zprávy
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[ALERT FAILED] {DateTime.Now:HH:mm:ss} Chyba při odesílání alertu: {ex.Message}");
                            }
                        });
                    }

                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    break;
                case LogLevel.Debug:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    break;
                case LogLevel.DiscordClientLog:
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    break;
                case LogLevel.Success:
                    Console.ForegroundColor = ConsoleColor.Green;
                    break;
            }

            Console.WriteLine(fullMessage);

            if (level == LogLevel.Error)
            {
                Console.WriteLine(new string('^', fullMessage.Length));
            }

            Console.ResetColor();
            return Task.CompletedTask;
        }
    }
}
