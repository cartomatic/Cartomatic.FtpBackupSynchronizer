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
                    
                    try
                    {
                        Log($"Listing contents of {_ftpBaseSettings.GetEffectiveUri()}...");
                        foreach (var entry in await _ftpBaseSettings.GetEntriesAsync())
                        {
                            try
                            {
                                var lastUpdated = await _ftpBaseSettings.GetEntryLastModifiedTimeAsync(entry);
                                Log($"{entry}: {lastUpdated:F}");
                            }
                            catch (Exception ex)
                            {
                                //note: this may mean this is a folder or there are some permission issues involved
                                LogErr($"Failed to obtain '{entry}' last modified date: {ex.Message}");
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
            }
            catch (Exception ex)
            {
                LogErr($"Could not connect");
                LogErr(ex);
            }

            SayGoodBy();
        }
    }
}
