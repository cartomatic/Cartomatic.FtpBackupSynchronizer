// See https://aka.ms/new-console-template for more information
using Cartomatic;
var synchronizer = new FtpBackupSynchronizer();

//await synchronizer.Test();


await synchronizer.DoWorkAsync();