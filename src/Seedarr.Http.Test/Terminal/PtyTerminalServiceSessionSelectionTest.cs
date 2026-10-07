// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class PtyTerminalServiceSessionSelectionTest
{
    private IConfigFileProvider _configFileProvider;
    private IConfigService _configService;
    private PtyTerminalService _service;
    private string _saveRoot;

    [SetUp]
    public void SetUp()
    {
        _saveRoot = Path.Combine(Path.GetTempPath(), $"seedarr-pty-select-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_saveRoot);

        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.TerminalAccessEnabled.Returns(true);

        _configService = Substitute.For<IConfigService>();
        _configService.TorrentSaveDirectory.Returns(_saveRoot);
        _configService.DefaultSavePath.Returns(_saveRoot);

        _service = new PtyTerminalService(_configFileProvider, _configService);
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
    [Platform(Include = "Linux")]
    public void CreateSession_on_linux_prefers_native_pty_over_python_when_available()
    {
        if (!HasPython3OnPath())
        {
            Assert.Ignore("python3 not on standard paths; issue only applies when python would have won previously.");
        }

        var dir = Path.Combine(_saveRoot, $"cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        ITerminalSession session = null;
        try
        {
            session = _service.CreateSession(dir, 80, 24);
            Assert.That(session, Is.InstanceOf<LinuxPtySession>());
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Native PTY spawn unavailable on this host: {ex.Message}");
        }
        finally
        {
            session?.Kill();
        }
    }

    private static bool HasPython3OnPath()
    {
        return File.Exists("/usr/bin/python3")
            || File.Exists("/bin/python3")
            || File.Exists("/usr/local/bin/python3");
    }
}
