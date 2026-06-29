// See https://aka.ms/new-console-template for more information
using Cartomatic;
var synchronizer = new FtpBackupSynchronizer();

if(args.Any(x => x.ToLower() == "test"))
    await synchronizer.TestAsync();
else
    await synchronizer.DoWorkAsync();