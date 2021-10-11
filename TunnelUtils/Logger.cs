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
            var consoleTarget = new ColoredConsoleTarget("console")
            {
                Name = "console",
                Layout = "${date}|${level:uppercase=true}|${message}",
            };
            config.AddRule(LogLevel.Info, LogLevel.Warn, consoleTarget, "console");
            var consoleTargetForErrors = new ColoredConsoleTarget("console")
            {
                Name = "error_console",
                Layout = "${date}|${level:uppercase=true}|${message}|${exception}|${stacktrace}",
            };
            config.AddRule(LogLevel.Error, LogLevel.Fatal, consoleTargetForErrors,"console");

            var logFileTarget = new FileTarget("logfile")
            {
                Name = "logfile",
                Layout = "${date}|${threadid}|${level:uppercase=true}|${message}",
                AutoFlush = true,
                FileName = @"logs\tunnel.log",
                ArchiveEvery = FileArchivePeriod.Day,
                //ConcurrentWrites = true
            };

            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logFileTarget, "main");


            LogManager.Configuration = config;
        }


        public static void Info(string text)
        {
            LogManager.GetLogger("console").Info(text);
            LogManager.GetLogger("main").Info(text);
        }
        public static void Debug(string text)
        {
            LogManager.GetLogger("console").Debug(text);
            LogManager.GetLogger("main").Debug(text);
        }
        public static void Trace(string text)
        {
            LogManager.GetLogger("console").Trace(text);
            LogManager.GetLogger("main").Trace(text);
        }
        public static void Error(Exception ex, string text)
        {
            LogManager.GetLogger("console").Error(ex, text);
            LogManager.GetLogger("main").Error(ex, text);
        }
        public static void Error(string text)
        {
            LogManager.GetLogger("console").Error(text);
            LogManager.GetLogger("main").Error(text);
        }
        public static void Fatal(string text)
        {
            LogManager.GetLogger("console").Fatal(text);
            LogManager.GetLogger("main").Fatal(text);
        }
        public static void Fatal(Exception ex, string text)
        {
            LogManager.GetLogger("console").Fatal(ex, text);
            LogManager.GetLogger("main").Fatal(ex, text);
        }
    }
}
