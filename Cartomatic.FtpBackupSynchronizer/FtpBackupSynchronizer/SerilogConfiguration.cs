using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cartomatic.Utils;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Cartomatic
{
    /// <summary>
    /// Serilog configuration utils
    /// </summary>
    public partial class FtpBackupSynchronizer
    {
        internal class SerilogCfg
        {
            public Dictionary<string, LogEventLevel?> Sinks { get; set; }
        }

        /// <summary>
        /// Configures serilog in a generic way for api
        /// </summary>
        private static void ConfigureSerilog(IConfiguration cfg)
        {
            var logsDir = cfg.GetSection("Logs").Get<string>().SolvePath();
            var logCfg = cfg.GetSection("SerilogConfiguration").Get<SerilogCfg>();

            //https://github.com/serilog/serilog/wiki/Formatting-Output
            //var outputTpl = "{Timestamp:HH:mm:ss}\t[{Level:u3}]\t{App}\t{Message}\t{Exception}\t{Properties:j}{NewLine}";
            //var outputTpl = "{Timestamp:HH:mm:ss} [{Level:u3}] {App} {Message}{NewLine}{Exception}";
            var outputTpl = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";


            Serilog.Log.Logger = new LoggerConfiguration()

                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Verbose()
                .WriteTo.File(
                    Path.Combine(logsDir, $"ftp_backup_synchronizer_.log"),
                    restrictedToMinimumLevel: logCfg?.Sinks != null && logCfg.Sinks.ContainsKey(nameof(Serilog.Sinks.File)) ? logCfg.Sinks[nameof(Serilog.Sinks.File)] ?? LogEventLevel.Warning : LogEventLevel.Warning, rollingInterval: RollingInterval.Day, flushToDiskInterval: TimeSpan.FromMinutes(5),
                    outputTemplate: outputTpl
                )
                .WriteTo.Console(
                    restrictedToMinimumLevel: logCfg?.Sinks != null && logCfg.Sinks.ContainsKey(nameof(Serilog.ConsoleLoggerConfigurationExtensions.Console)) ? logCfg.Sinks[nameof(Serilog.ConsoleLoggerConfigurationExtensions.Console)] ?? LogEventLevel.Verbose : LogEventLevel.Verbose,
                    outputTemplate: outputTpl,
                    theme: AnsiConsoleTheme.Code
                )
                .CreateLogger();
        }
    }
}
