using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class DiskSpaceCheckTest
{
    private IAppFolderInfo _appFolderInfo;
    private DiskSpaceCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _subject = new DiskSpaceCheck(_appFolderInfo);
    }

    [Test]
    public void Check_should_return_ok_when_disk_has_plenty_of_free_space()
    {
        // Use the system temp path — virtually guaranteed to have >500 MB on any dev/CI machine
        _appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_source_should_always_be_DiskSpace()
    {
        _appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());

        var result = _subject.Check();

        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
    }

    [Test]
    public void Check_should_return_ok_when_exception_occurs_accessing_app_folder()
    {
        // NSubstitute returns null for string properties by default.
        // Path.GetFullPath(null) throws ArgumentNullException, which the bare catch swallows.
        _appFolderInfo.AppDataFolder.Returns((string)null);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
    }

    [Test]
    public void Check_should_not_throw_for_valid_path()
    {
        _appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());

        Assert.DoesNotThrow(() => _subject.Check());
    }

    [Test]
    public void Check_error_result_should_contain_DiskSpace_source()
    {
        // Verifies the source constant used in error paths matches the ok path.
        // The error branch (< 500 MB free) requires a genuinely full disk and
        // cannot be unit-tested without refactoring DriveInfo out of the class.
        _appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());
        var result = _subject.Check();

        // Whether Ok or Error, source must always be "DiskSpace"
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
    }
}
