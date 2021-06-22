using NLog;
using NLog.Config;
using NLog.Targets;
using System;
namespace TunnelUtils
{
    public class Logger 
    {
        static Logger()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ColoredConsoleTarget
            {
                Name = "console",
                Layout = "${date}|${level:uppercase=true}|${message}",
            };
            config.AddRule(LogLevel.Info, LogLevel.Warn, consoleTarget);
            var consoleTargetForErrors = new ColoredConsoleTarget
            {
                Name = "error_console",
                Layout = "${date}|${level:uppercase=true}|${message}|${exception}|${stacktrace}",
            };
            config.AddRule(LogLevel.Error, LogLevel.Fatal, consoleTargetForErrors);

            LogManager.Configuration = config;
        }


        public static void Info(string text)
        {
            LogManager.GetCurrentClassLogger().Info(text);
        }
        public static void Debug(string text)
        {
            LogManager.GetCurrentClassLogger().Debug(text);
        }
        public static void Error(Exception ex, string text)
        {
            LogManager.GetCurrentClassLogger().Error(ex, text);
        }
        public static void Error(string text)
        {
            LogManager.GetCurrentClassLogger().Error(text);
        }
        public static void Fatal(string text)
        {
            LogManager.GetCurrentClassLogger().Fatal(text);
        }
        public static void Fatal(Exception ex, string text)
        {
            LogManager.GetCurrentClassLogger().Fatal(ex, text);
        }
    }
}
