using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Backup;

namespace NzbDrone.Core.Test.Backup;

[TestFixture]
public class BackupJobTest
{
    private IBackupService _backupService;
    private BackupJob _job;

    [SetUp]
    public void SetUp()
    {
        _backupService = Substitute.For<IBackupService>();
        _job = new BackupJob(_backupService);
    }

    [Test]
    public void Execute_should_invoke_CreateBackup_with_Scheduled_type()
    {
        _job.Execute();

        _backupService.Received(1).CreateBackup(BackupType.Scheduled);
    }

    [Test]
    public void Execute_command_should_invoke_CreateBackup_with_command_type()
    {
        var command = new BackupCommand { Type = BackupType.Manual };

        _job.Execute(command);

        _backupService.Received(1).CreateBackup(BackupType.Manual);
    }

    [Test]
    public void Execute_command_should_default_to_Manual_when_command_is_null()
    {
        _job.Execute((BackupCommand)null);

        _backupService.Received(1).CreateBackup(BackupType.Manual);
    }

    [Test]
    public void DefaultInterval_should_be_1440()
    {
        Assert.That(_job.DefaultInterval, Is.EqualTo(1440));
    }
}
