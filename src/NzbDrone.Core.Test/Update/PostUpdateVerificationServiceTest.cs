using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.Update;

[TestFixture]
public class PostUpdateVerificationServiceTest
{
    private string _tempDir;
    private string _backupDir;
    private string _targetDir;
    private string _stateFilePath;
    private IAppFolderInfo _appFolderInfo;
    private IHealthCheckService _healthCheckService;
    private IEventAggregator _eventAggregator;
    private PostUpdateVerificationService _service;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SeedarrTest_" + Guid.NewGuid().ToString("N"));
        _backupDir = Path.Combine(_tempDir, "backup");
        _targetDir = Path.Combine(_tempDir, "target");
        _stateFilePath = Path.Combine(_tempDir, "update_state.json");

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_backupDir);
        Directory.CreateDirectory(_targetDir);

        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDir);
        _appFolderInfo.StartUpFolder.Returns(_targetDir);

        _healthCheckService = Substitute.For<IHealthCheckService>();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _service = new PostUpdateVerificationService(
            _appFolderInfo,
            _healthCheckService,
            _eventAggregator,
            _targetDir,
            _stateFilePath);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best-effort test cleanup
        }
    }

    [Test]
    public void UpdateState_serialization_and_deserialization_from_json()
    {
        var original = new UpdateState
        {
            PreviousVersion = "1.0.0",
            TargetVersion = "1.1.0",
            State = UpdateLifecycleState.PendingVerification,
            InitiatedAt = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc),
            VerificationTimeoutSeconds = 90,
            BackupDirectory = _backupDir,
            ErrorMessage = null,
        };

        _service.SaveUpdateState(original);

        Assert.That(File.Exists(_stateFilePath), Is.True);

        var retrieved = _service.GetUpdateState();
        Assert.That(retrieved.PreviousVersion, Is.EqualTo(original.PreviousVersion));
        Assert.That(retrieved.TargetVersion, Is.EqualTo(original.TargetVersion));
        Assert.That(retrieved.State, Is.EqualTo(UpdateLifecycleState.PendingVerification));
        Assert.That(retrieved.VerificationTimeoutSeconds, Is.EqualTo(90));
        Assert.That(retrieved.BackupDirectory, Is.EqualTo(_backupDir));
        Assert.That(retrieved.ErrorMessage, Is.Null);
    }

    [Test]
    public void GetUpdateState_should_return_default_idle_state_when_file_not_found()
    {
        if (File.Exists(_stateFilePath))
        {
            File.Delete(_stateFilePath);
        }

        var state = _service.GetUpdateState();
        Assert.That(state.State, Is.EqualTo(UpdateLifecycleState.Idle));
        Assert.That(state.TargetVersion, Is.Empty);
    }

    [Test]
    public void StageUpdate_should_create_staging_state_and_persist_to_json()
    {
        var staged = _service.StageUpdate("2.0.0", _backupDir);

        Assert.That(staged.State, Is.EqualTo(UpdateLifecycleState.Staging));
        Assert.That(staged.TargetVersion, Is.EqualTo("2.0.0"));
        Assert.That(staged.BackupDirectory, Is.EqualTo(_backupDir));

        var loaded = _service.GetUpdateState();
        Assert.That(loaded.State, Is.EqualTo(UpdateLifecycleState.Staging));
        Assert.That(loaded.TargetVersion, Is.EqualTo("2.0.0"));
        Assert.That(loaded.BackupDirectory, Is.EqualTo(_backupDir));
    }

    [Test]
    public async Task VerifyUpdateAsync_should_complete_successfully_when_health_checks_pass()
    {
        await File.WriteAllTextAsync(Path.Combine(_backupDir, "app.bin"), "previous-binary");

        _service.StageUpdate("1.2.0", _backupDir);
        _healthCheckService.PerformChecks().Returns(new List<HealthCheckResult>
        {
            HealthCheckResult.Ok("Database"),
            HealthCheckResult.Ok("Indexers"),
        });

        var result = await _service.VerifyUpdateAsync();

        Assert.That(result, Is.True);
        var state = _service.GetUpdateState();
        Assert.That(state.State, Is.EqualTo(UpdateLifecycleState.Completed));
        Assert.That(state.ErrorMessage, Is.Null);

        Assert.That(Directory.Exists(_backupDir), Is.False);

        _eventAggregator.Received().PublishEvent(Arg.Is<ApplicationUpdatedEvent>(e =>
            e.NewVersion == "1.2.0"));
    }

    [Test]
    public async Task VerifyUpdateAsync_should_trigger_rollback_and_restore_files_when_health_check_fails()
    {
        await File.WriteAllTextAsync(Path.Combine(_backupDir, "app.bin"), "original-binary-content");
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "app.bin"), "corrupt-binary-content");

        _service.StageUpdate("1.2.0", _backupDir);
        _healthCheckService.PerformChecks().Returns(new List<HealthCheckResult>
        {
            HealthCheckResult.Error("Database", "Connection timeout"),
        });

        var result = await _service.VerifyUpdateAsync();

        Assert.That(result, Is.False);
        var state = _service.GetUpdateState();
        Assert.That(state.State, Is.EqualTo(UpdateLifecycleState.RolledBack));
        Assert.That(state.ErrorMessage, Does.Contain("Database: Connection timeout"));

        var restoredContent = await File.ReadAllTextAsync(Path.Combine(_targetDir, "app.bin"));
        Assert.That(restoredContent, Is.EqualTo("original-binary-content"));

        _eventAggregator.Received().PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "UpdateRollback"));
    }

    [Test]
    public async Task VerifyUpdateAsync_should_trigger_rollback_when_health_check_throws_exception()
    {
        await File.WriteAllTextAsync(Path.Combine(_backupDir, "app.dll"), "good-dll");
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "app.dll"), "bad-dll");

        _service.StageUpdate("1.2.0", _backupDir);
        _healthCheckService.PerformChecks().Throws(new InvalidOperationException("Fatal crash during check"));

        var result = await _service.VerifyUpdateAsync();

        Assert.That(result, Is.False);
        var state = _service.GetUpdateState();
        Assert.That(state.State, Is.EqualTo(UpdateLifecycleState.RolledBack));
        Assert.That(state.ErrorMessage, Does.Contain("Fatal crash during check"));

        var restoredContent = await File.ReadAllTextAsync(Path.Combine(_targetDir, "app.dll"));
        Assert.That(restoredContent, Is.EqualTo("good-dll"));
    }

    [Test]
    public async Task VerifyUpdateAsync_should_trigger_rollback_when_verification_times_out()
    {
        await File.WriteAllTextAsync(Path.Combine(_backupDir, "app.dll"), "timeout-backup");
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "app.dll"), "timeout-target");

        var timedOutState = new UpdateState
        {
            PreviousVersion = "1.0.0",
            TargetVersion = "1.1.0",
            State = UpdateLifecycleState.PendingVerification,
            InitiatedAt = DateTime.UtcNow.AddMinutes(-5),
            VerificationTimeoutSeconds = 60,
            BackupDirectory = _backupDir,
        };
        _service.SaveUpdateState(timedOutState);

        var result = await _service.VerifyUpdateAsync();

        Assert.That(result, Is.False);
        var state = _service.GetUpdateState();
        Assert.That(state.State, Is.EqualTo(UpdateLifecycleState.RolledBack));
        Assert.That(state.ErrorMessage, Does.Contain("timed out"));

        var restoredContent = await File.ReadAllTextAsync(Path.Combine(_targetDir, "app.dll"));
        Assert.That(restoredContent, Is.EqualTo("timeout-backup"));
    }

    [Test]
    public async Task RollbackAsync_should_restore_nested_directories_and_files()
    {
        var subDir = Path.Combine(_backupDir, "sub");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.txt"), "nested-content");

        _service.StageUpdate("1.5.0", _backupDir);

        await _service.RollbackAsync("Manual rollback test");

        var targetNested = Path.Combine(_targetDir, "sub", "nested.txt");
        Assert.That(File.Exists(targetNested), Is.True);
        var restoredContent = await File.ReadAllTextAsync(targetNested);
        Assert.That(restoredContent, Is.EqualTo("nested-content"));

        var state = _service.GetUpdateState();
        Assert.That(state.State, Is.EqualTo(UpdateLifecycleState.RolledBack));
        Assert.That(state.ErrorMessage, Is.EqualTo("Manual rollback test"));
    }
}
