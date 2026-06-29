using NLog;
using NLog.Config;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Core.Test.Instrumentation;

[TestFixture]
public class RingBufferTargetTest
{
    [TearDown]
    public void TearDown()
    {
        LogManager.Configuration = null;
    }

    private Logger ConfigureAndGetLogger(RingBufferTarget target)
    {
        var config = new LoggingConfiguration();
        config.AddTarget("ringbuffer", target);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, target);
        LogManager.Configuration = config;
        return LogManager.GetLogger("TestLogger");
    }

    [Test]
    public void Capacity_should_return_default_value()
    {
        var target = new RingBufferTarget();

        Assert.That(target.Capacity, Is.EqualTo(2048));
    }

    [Test]
    public void Capacity_should_return_custom_value()
    {
        var target = new RingBufferTarget(10);

        Assert.That(target.Capacity, Is.EqualTo(10));
    }

    [Test]
    public void GetEntries_should_return_empty_when_nothing_written()
    {
        var target = new RingBufferTarget();

        var entries = target.GetEntries(10, LogLevel.Trace);

        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void Write_should_store_log_entry()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Info("test message");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void Write_should_store_message()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Info("hello");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries[0].Message, Is.EqualTo("hello"));
    }

    [Test]
    public void Write_should_store_level()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Info("test");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries[0].Level, Is.EqualTo("Info"));
    }

    [Test]
    public void Write_should_store_logger_name()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Info("test");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries[0].Logger, Is.EqualTo("TestLogger"));
    }

    [Test]
    public void GetEntries_should_filter_by_minimum_level()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Debug("debug message");
        logger.Error("error message");

        var entries = target.GetEntries(10, LogLevel.Error);
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Message, Is.EqualTo("error message"));
    }

    [Test]
    public void GetEntries_should_limit_count()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        for (var i = 0; i < 5; i++)
        {
            logger.Info($"message {i}");
        }

        var entries = target.GetEntries(2, LogLevel.Trace);
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].Message, Is.EqualTo("message 3"));
        Assert.That(entries[1].Message, Is.EqualTo("message 4"));
    }

    [Test]
    public void Write_should_wrap_around_when_buffer_full()
    {
        var target = new RingBufferTarget(3);
        var logger = ConfigureAndGetLogger(target);

        for (var i = 0; i < 5; i++)
        {
            logger.Info($"message {i}");
        }

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries, Has.Count.EqualTo(3));
    }

    [Test]
    public void Write_should_preserve_chronological_order_after_wrap()
    {
        var target = new RingBufferTarget(3);
        var logger = ConfigureAndGetLogger(target);

        for (var i = 0; i < 5; i++)
        {
            logger.Info($"message {i}");
        }

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries[0].Message, Is.EqualTo("message 2"));
    }

    [Test]
    public void GetEntries_should_return_entries_in_chronological_order()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Info("A");
        logger.Info("B");
        logger.Info("C");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries[0].Message, Is.EqualTo("A"));
        Assert.That(entries[1].Message, Is.EqualTo("B"));
        Assert.That(entries[2].Message, Is.EqualTo("C"));
    }
}
