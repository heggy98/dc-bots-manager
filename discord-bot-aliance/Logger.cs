using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace discord_bot_aliance
{
    public class Logger : ILogger
    {
        public Logger()
        {
            
        }
        public enum LogLevel
        {
            Info,
            Warning,
            Error,
            Debug
        }

        public Task Consol(string message, LogLevel level = LogLevel.Info)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var fullMessage = $"[{level}] {timestamp} {message}";

            switch (level)
            {
                case LogLevel.Info:
                    Console.ForegroundColor = ConsoleColor.Green;
                    break;
                case LogLevel.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogLevel.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("*-*-* Niečo zle *-*-*");
                    break;
                case LogLevel.Debug:
                    Console.ForegroundColor = ConsoleColor.Cyan;
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
