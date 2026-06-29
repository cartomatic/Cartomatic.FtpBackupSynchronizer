using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        /// <summary>
        /// Performs a FTP accessibility test - connects to an FTP server with given credentials and list contents of the contexted directory
        /// </summary>
        /// <returns></returns>
        public async Task TestAsync()
        {
            _processingErrors = new List<string>();

            SayHello();

            Log($"SELF-TEST{_nl}");

            try
            {
                Log("Trying to connect...");
                if (await _ftpBaseSettings.CanConnectAsync())
                {
                    Log($"Connection ok...{_nl}");

                    await ListEntries(_ftpBaseSettings);
                    await ListEntries1(_ftpBaseSettings);
                }

                var input = _cfg.GetSection("Backup").Get<List<BackupConfiguration>>();
                foreach (var backupConfiguration in input)
                {
                    var ftpRequestSettings = _ftpBaseSettings.Clone();
                    ftpRequestSettings.SubPath = backupConfiguration.DestinationPath;

                    await ListEntries(ftpRequestSettings);
                    await ListEntries1(ftpRequestSettings);
                }
            }
            catch (Exception ex)
            {
                LogErr($"Could not connect");
                LogErr(ex);
                DumpWebExceptionDetails(ex);
            }

            SayGoodBy();
        }

        private async Task ListEntries(IFtpRequestSettings ftpRequestSettings)
        {
            try
            {
                Log($"Listing contents of {ftpRequestSettings.GetEffectiveUri()}...");
                foreach (var entry in await ftpRequestSettings.GetEntriesAsync())
                {
                    try
                    {
                        Log($"{entry}");
                        var lastUpdated = await ftpRequestSettings.GetEntryLastModifiedTimeAsync(entry);
                        Log($"Last updated: {lastUpdated:F}");
                    }
                    catch (Exception ex)
                    {
                        //note: this may mean this is a folder or there are some permission issues involved
                        LogErr($"Failed to obtain '{entry}' last modified date: {ex.Message}");
                        DumpWebExceptionDetails(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogErr($"Failed to obtain ftp entries");
                LogErr(ex);
            }
            Log($"Listing complete{_nl}");
        }

        private async Task ListEntries1(IFtpRequestSettings ftpRequestSettings)
        {
            try
            {
                Log($"Listing contents of {ftpRequestSettings.GetEffectiveUri()}...");
                foreach (var entry in await ftpRequestSettings.GetEntriesDetailedAsync())
                {
                    Log(entry);
                }
            }
            catch (Exception ex)
            {
                LogErr($"Failed to obtain ftp entries");
                LogErr(ex);
                DumpWebExceptionDetails(ex);
            }
            Log($"Listing complete{_nl}");
        }
    }
}
