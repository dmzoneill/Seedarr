using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Instrumentation;

namespace NzbDrone.Core.Test.Instrumentation;

[TestFixture]
public class FileDescriptorProviderTest
{
    private string _tempDir;
    private string _limitsFile;
    private string _fdDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fd_test_" + Guid.NewGuid().ToString("N"));
        _limitsFile = Path.Combine(_tempDir, "limits");
        _fdDir = Path.Combine(_tempDir, "fd");

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_fdDir);
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
            // Ignore cleanup failures in test temp dir
        }
    }

    [Test]
    public void GetMaxFileDescriptors_parses_soft_limit_correctly()
    {
        var limitsContent = @"Limit                     Soft Limit           Hard Limit           Units     
Max cpu time              unlimited            unlimited            seconds   
Max file size             unlimited            unlimited            bytes     
Max data size             unlimited            unlimited            bytes     
Max open files            1024                 524288               files     
Max locked memory         65536                65536                bytes     ";

        File.WriteAllText(_limitsFile, limitsContent);

        var provider = new FileDescriptorProvider(_fdDir, _limitsFile, () => 42, () => false);

        Assert.That(provider.GetMaxFileDescriptors(), Is.EqualTo(1024));
    }

    [Test]
    public void GetMaxFileDescriptors_returns_negative_one_when_unlimited()
    {
        var limitsContent = @"Limit                     Soft Limit           Hard Limit           Units     
Max open files            unlimited            unlimited            files     ";

        File.WriteAllText(_limitsFile, limitsContent);

        var provider = new FileDescriptorProvider(_fdDir, _limitsFile, () => 42, () => false);

        Assert.That(provider.GetMaxFileDescriptors(), Is.EqualTo(-1));
    }

    [Test]
    public void GetMaxFileDescriptors_returns_negative_one_when_file_missing()
    {
        var nonExistentPath = Path.Combine(_tempDir, "does_not_exist");
        var provider = new FileDescriptorProvider(_fdDir, nonExistentPath, () => 42, () => false);

        Assert.That(provider.GetMaxFileDescriptors(), Is.EqualTo(-1));
    }

    [Test]
    public void GetMaxFileDescriptors_returns_negative_one_on_windows()
    {
        File.WriteAllText(_limitsFile, "Max open files            1024                 524288               files     ");

        var provider = new FileDescriptorProvider(_fdDir, _limitsFile, () => 42, () => true);

        Assert.That(provider.GetMaxFileDescriptors(), Is.EqualTo(-1));
    }

    [Test]
    public void GetOpenFileDescriptorCount_counts_directory_entries()
    {
        File.WriteAllText(Path.Combine(_fdDir, "0"), "");
        File.WriteAllText(Path.Combine(_fdDir, "1"), "");
        File.WriteAllText(Path.Combine(_fdDir, "2"), "");
        File.WriteAllText(Path.Combine(_fdDir, "3"), "");

        var provider = new FileDescriptorProvider(_fdDir, _limitsFile, () => 42, () => false);

        Assert.That(provider.GetOpenFileDescriptorCount(), Is.EqualTo(4));
    }

    [Test]
    public void GetOpenFileDescriptorCount_falls_back_to_handle_count_when_fd_dir_missing()
    {
        var missingDir = Path.Combine(_tempDir, "missing_fd");
        var provider = new FileDescriptorProvider(missingDir, _limitsFile, () => 99, () => false);

        Assert.That(provider.GetOpenFileDescriptorCount(), Is.EqualTo(99));
    }

    [Test]
    public void GetOpenFileDescriptorCount_uses_handle_count_on_windows()
    {
        File.WriteAllText(Path.Combine(_fdDir, "0"), "");

        var provider = new FileDescriptorProvider(_fdDir, _limitsFile, () => 150, () => true);

        Assert.That(provider.GetOpenFileDescriptorCount(), Is.EqualTo(150));
    }

    [Test]
    public void GetFileDescriptorUsagePercentage_calculates_correct_percentage()
    {
        for (var i = 0; i < 256; i++)
        {
            File.WriteAllText(Path.Combine(_fdDir, i.ToString()), "");
        }

        File.WriteAllText(_limitsFile, "Max open files            1024                 524288               files     ");

        var provider = new FileDescriptorProvider(_fdDir, _limitsFile, () => 42, () => false);

        var percentage = provider.GetFileDescriptorUsagePercentage();
        Assert.That(percentage, Is.Not.Null);
        Assert.That(percentage.Value, Is.EqualTo(25.0));
    }

    [Test]
    public void GetFileDescriptorUsagePercentage_returns_null_when_max_is_negative()
    {
        var provider = new FileDescriptorProvider(_fdDir, _limitsFile, () => 42, () => true);

        Assert.That(provider.GetFileDescriptorUsagePercentage(), Is.Null);
    }

    [Test]
    public void Default_provider_runs_without_throwing()
    {
        var provider = new FileDescriptorProvider();
        var count = provider.GetOpenFileDescriptorCount();
        var max = provider.GetMaxFileDescriptors();
        var usage = provider.GetFileDescriptorUsagePercentage();

        Assert.That(count, Is.GreaterThanOrEqualTo(-1));
        if (max > 0 && count >= 0)
        {
            Assert.That(usage, Is.Not.Null);
            Assert.That(usage.Value, Is.GreaterThanOrEqualTo(0.0));
        }
    }
}
