﻿using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Cartomatic.Utils;
using Cartomatic.Utils.Email;
using Cartomatic.Utils.Ftp;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using static Cartomatic.FtpBackupSynchronizer;

namespace Cartomatic
{
    public partial class FtpBackupSynchronizer
    {
        internal class BackupConfiguration
        {
            /// <summary>
            /// Directory to backup
            /// </summary>
            public string InputPath { get; set; }

            /// <summary>
            /// Destination directory
            /// </summary>
            public string DestinationPath { get; set; }

            /// <summary>
            /// Whether or not input content should be zipped; zips all the files and sub directories
            /// </summary>
            public bool? Compress { get; set; }

            /// <summary>
            /// Whether or not nested files should also be handled
            /// </summary>
            public bool? Nested { get; set; }

            /// <summary>
            /// File search pattern - if specified, a subset of files is handled
            /// </summary>
            public string FileExtensionFilter { get; set; }

            /// <summary>
            /// Compression level
            /// </summary>
            public CompressionLevel? CompressionLevel { get; set; }

            /// <summary>
            /// Whether or not should delete input directory contents that is older than days specified
            /// </summary>
            public int? InputDeleteOlderThanDays { get; set; }

            /// <summary>
            /// Whether or not should delete destination files older than days specified
            /// </summary>
            public int? DestinationDeleteOlderThanDays { get; set; }


        }

        public async Task DoWorkAsync()
        {
            _processingErrors = new List<string>();
            _filesUploaded = new List<string>();
            _filesCleanedUpLocally = new List<string>();
            _filesCleanedUpFtp = new List<string>();

            SayHello();

            try
            {
                Log("Getting backup configuration...");

                var input = _cfg.GetSection("Backup").Get<List<BackupConfiguration>>();

                if (input == null || input.Count == 0)
                    throw new Exception("No backup configuration found");

                Log($"{input.Count} backup configurations found{_nl}");

                foreach (var backupConfiguration in input)
                {
                    await ProcessBackupAsync(backupConfiguration);
                }
            }
            catch (Exception ex)
            {
                LogErr(ex);
            }

            //notify errors
            Log("Emaling notification...");
            await SendProcessingLogEmail();
            Log($"Email(s) sent{_nl}");

            SayGoodBy();
        }

        private async Task SendProcessingLogEmail()
        {
            var emailAcount = _cfg.GetSection("EmailSender").Get<EmailAccount>();
            var emailRecipients = _cfg.GetSection("EmailRecipients").Get<List<string>>();
            var ctx = _cfg.GetSection("EmailContext").Get<string>();

            var title = $"{ctx} ::  PROCESSING LOG";
            if (_processingErrors.Count > 0)
                title += " :: ERRORS OCCURED";


            var emailTemplate = new EmailTemplate
            {
                Title = title,
                Body = @$"{GetFilesUploadedLog()}

{GetFilesCleanedUpFtpLog()}

{GetFilesCleanedUpLog()}

{GetErrorLog()}",
                IsBodyHtml = false
            };

            var emailSender = new Cartomatic.Utils.Email.EmailSender();
            foreach (var emailRecipient in emailRecipients)
            {
                await emailSender.SendAsync(emailAcount, emailTemplate, emailRecipient, (string msg) => Log(msg));
            }
        }

        private string GetFilesUploadedLog()
            => $@"Files uploaded:
{(_filesUploaded.Count > 0 ? string.Join(Environment.NewLine, _filesUploaded) : "---")}";

        private string GetFilesCleanedUpLog()
            => $@"Files cleaned up locally:
{(_filesCleanedUpLocally.Count > 0 ? string.Join(Environment.NewLine, _filesCleanedUpLocally) : "---")}";

        private string GetFilesCleanedUpFtpLog()
            => $@"Files cleand up FTP:
{(_filesCleanedUpFtp.Count > 0 ? string.Join(Environment.NewLine, _filesCleanedUpFtp) : "---")}
";

        private string GetErrorLog()
            => @$"Errors occurred during backup utility run:
{(_processingErrors.Count > 0 ? string.Join(Environment.NewLine, _processingErrors) : "---")}";
        

        /// <summary>
        /// Cleans up FTP files
        /// </summary>
        /// <param name="backupConfiguration"></param>
        /// <returns></returns>
        private async Task FtpCleanupAsync(BackupConfiguration backupConfiguration)
        {
            if (!backupConfiguration.DestinationDeleteOlderThanDays.HasValue ||
                backupConfiguration.DestinationDeleteOlderThanDays < 0)
            {
                return;
            }

            var ftpBase = _ftpBaseSettings.Clone();
            ftpBase.SubPath = backupConfiguration.DestinationPath;

            Log($"Cleaning up FTP files older than: {backupConfiguration.DestinationDeleteOlderThanDays} day(s)...");
            foreach (var entry in await ftpBase.GetEntriesAsync())
            {
                var lastModifyDate = await ftpBase.GetEntryLastModifiedTimeAsync(entry);
                if(new TimeSpan(DateTime.Now.Ticks - lastModifyDate.Ticks).TotalDays > backupConfiguration.DestinationDeleteOlderThanDays)
                {
                    Log($"Cleaning up: {ftpBase.GetEffectiveUri()}/{entry}");
                    if (await ftpBase.DeleteFileAsync(entry))
                    {
                        Log("Filed cleaned up");
                        _filesCleanedUpFtp.Add($"{ftpBase.GetEffectiveUri()}/{entry}");
                    }
                    else
                    {
                        LogErr($"Failed to clean FTP file!");
                    }
                }
            }
            Log("FTP file cleanup completed");
        }

        /// <summary>
        /// Cleans up local files
        /// </summary>
        /// <param name="backupConfiguration"></param>
        /// <returns></returns>
        private async Task LocalCleanupAsync(BackupConfiguration backupConfiguration)
        {
            if (!backupConfiguration.InputDeleteOlderThanDays.HasValue || backupConfiguration.InputDeleteOlderThanDays < 0 || !Directory.Exists(backupConfiguration.InputPath))
                return;

            Log($"Cleaning up local files older than: {backupConfiguration.InputDeleteOlderThanDays} day(s)...");

            var files = Directory.GetFiles(backupConfiguration.InputPath)
                .Where(f => new TimeSpan(DateTime.Now.Ticks - new FileInfo(f).CreationTime.Ticks).TotalDays > backupConfiguration.InputDeleteOlderThanDays);

            
            foreach (var file in files)
            {
                if (File.Exists(file))
                {
                    Log($"Cleaning up: {file}");
                    File.Delete(file);
                    _filesCleanedUpLocally.Add(file);
                }
            }

            Log("Local file cleanup completed");
        }

        /// <summary>
        /// Processes a backup cfg
        /// </summary>
        /// <param name="backupConfiguration"></param>
        /// <returns></returns>
        private async Task ProcessBackupAsync(BackupConfiguration backupConfiguration)
        {
            Log($"Processing backup for {backupConfiguration.InputPath}...");
            
            try
            {
                //ensure destination folder exists
                if (string.IsNullOrEmpty(backupConfiguration.InputPath))
                    throw new Exception($"Backup input dir empty");

                if (!Directory.Exists(backupConfiguration.InputPath))
                    throw new Exception("Backup input dir does not exist");


                Log("Ensuring destination dir...");
                var ftpBase = _ftpBaseSettings.Clone();
                
                if (!string.IsNullOrWhiteSpace(backupConfiguration.DestinationPath) &&
                    !await ftpBase.DirectoryExistsAsync(backupConfiguration.DestinationPath))
                {
                    await ftpBase.CreateDirectoryAsync(backupConfiguration.DestinationPath);
                }
                ftpBase.SubPath = backupConfiguration.DestinationPath;
                Log("Destination dir OK");


                var filesToUpload = new List<string>();
                var tmpFilesToCleanUp = new List<string>();
                

                if (backupConfiguration.Compress == true)
                {
                    var fileSystemEntries = Directory.GetFileSystemEntries(backupConfiguration.InputPath);

                    if (!fileSystemEntries.Any())
                    {
                        Log("Nothing to backup");
                    }
                    else
                    {
                        

                        //this should get the name of the actual directory...
                        var inputDir = Path.GetFileNameWithoutExtension(backupConfiguration.InputPath);
                        var zipFName = $"{inputDir.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.zip";

                        //prior to zipping, check for zip presence - no point in zipping a shitload of data if it will not be sent anyway
                        if (await ftpBase.FileExistsAsync(zipFName))
                        {
                            Log($"File already exists in FTP, skipping: {ftpBase.GetEffectiveUri()}/{zipFName}");
                        }
                        else
                        {
                            Log("Compressing data...");
                            var zipFPath = Path.Combine(_tmpDir, zipFName);

                            System.IO.Compression.ZipFile.CreateFromDirectory(
                                backupConfiguration.InputPath,
                                zipFPath,
                                backupConfiguration.CompressionLevel ?? CompressionLevel.Fastest,
                                false
                            );

                            filesToUpload.Add(zipFPath);
                            tmpFilesToCleanUp.Add(zipFPath);

                            Log("Data compressed");
                        }
                    }
                }
                else
                {
                    var files = Directory.GetFiles(
                        backupConfiguration.InputPath,
                        "*.*",
                        backupConfiguration.Nested == true
                            ? SearchOption.AllDirectories
                            : SearchOption.TopDirectoryOnly
                    );

                    if (!string.IsNullOrWhiteSpace(backupConfiguration.FileExtensionFilter))
                    {
                        var extensions = backupConfiguration.FileExtensionFilter.Split(',');
                        files = files.Where(f => extensions.Contains(Path.GetExtension(f).Substring(1))).ToArray();
                    }
                    

                    if (!files.Any())
                    {
                        Log("Nothing to backup");
                    }
                    else
                    {
                        //assume the files for backup have appropriate names; if need to change name for the file to reflect backup date, compress shite before upload!
                        filesToUpload.AddRange(files.Select(x => x.Replace(" ", "_")));
                    }
                }

                foreach (var fileToUpload in filesToUpload)
                {
                    var fileToUploadLocal = fileToUpload;
                    var fName = Path.GetFileName(fileToUpload);

                    var subDir = GetSubDir(fileToUpload, backupConfiguration.InputPath);
                    if (!string.IsNullOrWhiteSpace(subDir))
                    {
                        var fileClone = Path.Combine(
                            backupConfiguration.InputPath,
                            $"{subDir.Replace("/", "__")}__{fName}"
                        );
                        File.Copy(fileToUpload, fileClone);
                        tmpFilesToCleanUp.Add(fileClone);

                        fileToUploadLocal = fileClone;
                        fName = Path.GetFileName(fileToUploadLocal);
                    }

                    Log($"Uploading file: {fName}...");
                    if (await ftpBase.FileExistsAsync(fName))
                    {
                        Log($"File already exists!");
                    }
                    else
                    {
                        if (await ftpBase.UploadFileAsync(fileToUploadLocal))
                        {
                            Log("Upload completed");

                            Log("Verifying file consistency - downloading file...");
                            var tmpPath = Path.Combine(_tmpDir, $"{fName}.downloaded");
                            if (await ftpBase.DownloadFileAsync(fName, tmpPath))
                            {
                                tmpFilesToCleanUp.Add(tmpPath);

                                //compare sha of files!
                                if (ComputeFileSha(fileToUploadLocal) == ComputeFileSha(tmpPath))
                                {
                                    Log("File sha OK");
                                    _filesUploaded.Add(fileToUploadLocal);
                                }
                                else
                                {
                                    LogErr($"File sha MISMATCH!");
                                }
                            }
                            else
                            {
                                LogErr($"Failed to download file!");
                            }
                        }
                        else
                        {
                            LogErr($"Failed to upload!");
                        }
                    }
                }

                if (tmpFilesToCleanUp.Any())
                {
                    Log("Cleaning up tmp files");

                    foreach (var file in tmpFilesToCleanUp)
                    {
                        Log($"Deleting: {file}");
                        if (File.Exists(file))
                            File.Delete(file);
                    }

                    Log("Tmp files cleaned up");
                }

            }
            catch (Exception ex)
            {
                LogErr(ex);
            }

            await FtpCleanupAsync(backupConfiguration);
            await LocalCleanupAsync(backupConfiguration);

            Log($"Backup processed{_nl}");
        }

        /// <summary>
        /// Computes a file sha
        /// </summary>
        /// <param name="fName"></param>
        /// <returns></returns>
        private string ComputeFileSha(string fName)
        {
            using var sha = new SHA256Managed();
            using var fs = File.OpenRead(fName);
            var checksum = sha.ComputeHash(fs);
            return Convert.ToBase64String(checksum);
        }

        private string GetSubDir(string fileToUpload, string baseDir)
        {
            var fName = Path.GetFileName(fileToUpload);
            var subDir = fileToUpload
                .Replace(baseDir, string.Empty)
                .Replace(fName, string.Empty)
                .Replace("\\", "/");

            if (subDir.StartsWith("/"))
                subDir = subDir.Substring(1);
            if (subDir.EndsWith("/"))
                subDir = subDir.Substring(0, subDir.Length - 1);

            return subDir;
        }
    }
}
