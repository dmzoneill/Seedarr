// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class TerminalCwdResolverTest
{
    private string _saveRoot;
    private IConfigService _configService;

    [SetUp]
    public void SetUp()
    {
        _saveRoot = Path.Combine(Path.GetTempPath(), $"seedarr-terminal-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_saveRoot);

        _configService = Substitute.For<IConfigService>();
        _configService.TorrentSaveDirectory.Returns(_saveRoot);
        _configService.DefaultSavePath.Returns(_saveRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_saveRoot))
        {
            Directory.Delete(_saveRoot, recursive: true);
        }
    }

    [Test]
    public void ResolveWorkingDirectory_allows_subdirectory_of_save_root()
    {
        var sub = Path.Combine(_saveRoot, "nested");
        Directory.CreateDirectory(sub);

        var resolved = TerminalCwdResolver.ResolveWorkingDirectory(sub, _configService);
        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(sub)));
    }

    [Test]
    public void ResolveWorkingDirectory_rejects_path_outside_save_roots()
    {
        var outside = Path.GetTempPath();
        if (TerminalCwdResolver.IsPathUnderRoot(Path.GetFullPath(outside), Path.GetFullPath(_saveRoot)))
        {
            Assert.Ignore("Temp path is nested under save root on this host.");
        }

        var ex = Assert.Throws<ArgumentException>(() =>
            TerminalCwdResolver.ResolveWorkingDirectory(outside, _configService));
        Assert.That(ex!.Message, Does.Contain("torrent save paths"));
    }

    [Test]
    public void ResolveWorkingDirectory_rejects_traversal_outside_save_root()
    {
        var traversal = Path.Combine(_saveRoot, "..", "seedarr-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(traversal);
        try
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                TerminalCwdResolver.ResolveWorkingDirectory(traversal, _configService));
            Assert.That(ex!.Message, Does.Contain("torrent save paths"));
        }
        finally
        {
            if (Directory.Exists(traversal))
            {
                Directory.Delete(traversal, recursive: true);
            }
        }
    }

    [Test]
    public void ResolveWorkingDirectory_defaults_to_configured_save_root()
    {
        var resolved = TerminalCwdResolver.ResolveWorkingDirectory(null, _configService);
        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(_saveRoot)));
    }
}
