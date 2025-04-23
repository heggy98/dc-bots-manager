using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static discord_bot_aliance.Logger;

namespace discord_bot_aliance
{
    public interface ILogger
    {
        Task Consol(string message, LogLevel level = LogLevel.Info);
    }
}
