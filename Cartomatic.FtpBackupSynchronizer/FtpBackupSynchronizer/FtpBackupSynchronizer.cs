using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cartomatic.Utils;
using Cartomatic.Utils.Ftp;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Cartomatic
{
    public partial class FtpBackupSynchronizer
    {
        private readonly IConfiguration _cfg;
        private readonly FtpRequestSettings _ftpBaseSettings;

        private string _appName = "Cartomatic.FtpBackupSynchronizer";
        private string _version = "v1";

        private readonly ILogger _logger;

        private List<string> _processingErrors;
        private List<string> _filesUploaded;
        private List<string> _filesCleanedUpLocally;
        private List<string> _filesCleanedUpFtp;

        private readonly string _tmpDir;

        public FtpBackupSynchronizer()
        {
            _cfg = Cartomatic.Utils.NetCoreConfig.GetNetCoreConfig("appsettings.dev");
            _ftpBaseSettings = _cfg.GetSection("FtpCfg").Get<FtpRequestSettings>();

            _tmpDir = _cfg.GetSection("Tmp").Get<string>().SolvePath();
            if(!Directory.Exists(_tmpDir))
                Directory.CreateDirectory(_tmpDir);

            ConfigureSerilog(_cfg);
            _logger = Serilog.Log.Logger;
        }


        private static string _hr = Environment.NewLine + string.Join(string.Empty, Enumerable.Repeat('-', 100));
        private static string _nl = Environment.NewLine;
        
        private void Log(string msg, LogEventLevel lvl = LogEventLevel.Information)
        {
            switch (lvl)
            {
                case LogEventLevel.Verbose:
                    _logger.Verbose(msg);
                    break;
                case LogEventLevel.Debug:
                    _logger.Debug(msg);
                    break;
                case LogEventLevel.Information:
                    _logger.Information(msg);
                    break;
                case LogEventLevel.Warning:
                    _logger.Warning(msg);
                    break;
                case LogEventLevel.Error:
                    _logger.Error(msg);
                    break;
                case LogEventLevel.Fatal:
                    _logger.Fatal(msg);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lvl), lvl, null);
            }
        }

        private void LogErr(string msg)
        {
            Log(msg, LogEventLevel.Error);
            _processingErrors.Add(msg);
        }
        private void LogErr(Exception ex)
        {
            _logger.Error(ex, string.Empty);
            _processingErrors.Add(ex.Message);
        }

        private void SayHello()
        {
            Log($"{_appName} :: {_version}{_hr}{_nl}");
        }

        private void SayGoodBy()
        {
            Log($"DONE!{_hr}{_nl}{_nl}{_nl}{_nl}");
        }
    }
}
