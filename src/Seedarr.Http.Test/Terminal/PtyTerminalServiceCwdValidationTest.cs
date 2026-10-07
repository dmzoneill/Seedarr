// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class PtyTerminalServiceCwdValidationTest
{
    private IConfigFileProvider _configFileProvider;
    private PtyTerminalService _service;

    [SetUp]
    public void SetUp()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.TerminalAccessEnabled.Returns(true);
        _service = new PtyTerminalService(_configFileProvider);
    }

    [Test]
    public void CreateSession_with_nonexistent_cwd_throws_before_spawn()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"seedarr-missing-cwd-{Guid.NewGuid():N}");
        Assert.That(Directory.Exists(missing), Is.False);

        var ex = Assert.Throws<ArgumentException>(() => _service.CreateSession(missing, 80, 24));
        Assert.That(ex!.Message, Does.Contain("does not exist"));
        Assert.That(ex.ParamName, Is.EqualTo("cwd"));
    }

    [Test]
    public void CreateSession_with_file_path_throws_before_spawn()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"seedarr-cwd-file-{Guid.NewGuid():N}.txt");
        File.WriteAllText(filePath, "x");
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => _service.CreateSession(filePath, 80, 24));
            Assert.That(ex!.Message, Does.Contain("file").Or.Contain("does not exist"));
            Assert.That(ex.ParamName, Is.EqualTo("cwd"));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public void CreateSession_with_existing_directory_does_not_throw_for_cwd_validation()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"seedarr-valid-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            ITerminalSession session = null;
            try
            {
                session = _service.CreateSession(dir, 80, 24);
                Assert.That(session, Is.Not.Null);
            }
            catch (ArgumentException)
            {
                Assert.Fail("Existing directory should pass cwd validation");
            }
            catch (Exception ex)
            {
                // Spawn may fail in CI without PTY; cwd validation must have passed.
                Assert.That(ex.Message, Does.Not.Contain("does not exist"));
                Assert.That(ex.Message, Does.Not.Contain("not a directory"));
            }
            finally
            {
                session?.Kill();
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir);
            }
        }
    }
}
