using System;
using System.IO;
using System.Security;
using NUnit.Framework;
using NzbDrone.Core.Plugins;

namespace NzbDrone.Core.Test.Plugins;

[TestFixture]
public class PluginManifestTest
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_plugin_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Validate_should_succeed_for_valid_manifest()
    {
        var manifest = new PluginManifest
        {
            Id = "my-test-plugin",
            Name = "My Test Plugin",
            Version = "1.0.0",
            Entrypoint = "bin/plugin.sh",
            Type = "sidecar",
            Capabilities = new[] { "indexer", "notification" }
        };

        Assert.DoesNotThrow(() => manifest.Validate());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_should_throw_ArgumentException_when_Id_is_empty(string id)
    {
        var manifest = new PluginManifest
        {
            Id = id,
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = "plugin.sh"
        };

        Assert.Throws<ArgumentException>(() => manifest.Validate());
    }

    [TestCase("../evil")]
    [TestCase("evil/../../path")]
    [TestCase("evil\\..\\path")]
    [TestCase("evil/plugin")]
    [TestCase("evil\\plugin")]
    public void Validate_should_throw_SecurityException_when_Id_has_traversal(string invalidId)
    {
        var manifest = new PluginManifest
        {
            Id = invalidId,
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = "plugin.sh"
        };

        Assert.Throws<SecurityException>(() => manifest.Validate());
    }

    [Test]
    public void Validate_should_throw_SecurityException_when_Entrypoint_contains_dot_dot()
    {
        var manifest = new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = "../../../evil.sh"
        };

        Assert.Throws<SecurityException>(() => manifest.Validate());
    }

    [Test]
    public void ValidateAndResolveEntrypoint_should_resolve_path_within_plugin_dir()
    {
        var entrypoint = Path.Combine(_tempDir, "bin", "start.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(entrypoint)!);
        File.WriteAllText(entrypoint, "#!/bin/sh\nexit 0");

        var manifest = new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = "bin/start.sh"
        };

        var resolved = manifest.ValidateAndResolveEntrypoint(_tempDir);
        var expected = Path.GetFullPath(Path.Combine(_tempDir, "bin", "start.sh"));

        Assert.That(resolved, Is.EqualTo(expected));
    }

    [Test]
    public void ValidateAndResolveEntrypoint_should_throw_SecurityException_when_path_escapes_plugin_dir()
    {
        var manifest = new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = Path.Combine(Path.GetTempPath(), "outside.sh")
        };

        Assert.Throws<SecurityException>(() => manifest.ValidateAndResolveEntrypoint(_tempDir));
    }

    [Test]
    public void ValidateAndResolveEntrypoint_should_throw_SecurityException_when_symlink_points_outside_plugin_dir()
    {
        var outsideFile = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(outsideFile, "#!/bin/sh\necho outside");
        var symlinkPath = Path.Combine(_tempDir, "link_to_outside.sh");

        try
        {
            File.CreateSymbolicLink(symlinkPath, outsideFile);
        }
        catch
        {
            // If environment doesn't allow symlink creation, pass gracefully
            File.Delete(outsideFile);
            return;
        }

        try
        {
            var manifest = new PluginManifest
            {
                Id = "test-plugin",
                Name = "Test",
                Version = "1.0.0",
                Entrypoint = "link_to_outside.sh"
            };

            Assert.Throws<SecurityException>(() => manifest.ValidateAndResolveEntrypoint(_tempDir));
        }
        finally
        {
            if (File.Exists(outsideFile))
            {
                File.Delete(outsideFile);
            }
        }
    }
}
