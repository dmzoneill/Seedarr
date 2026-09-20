using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets.Wrappers;
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

    [Test]
    public void GetEntries_should_maintain_snapshot_integrity_when_new_entries_are_written()
    {
        var target = new RingBufferTarget(5);
        var logger = ConfigureAndGetLogger(target);

        logger.Info("msg-1");
        logger.Info("msg-2");
        logger.Info("msg-3");

        var snapshot = target.GetEntries(10, LogLevel.Trace);
        Assert.That(snapshot, Has.Count.EqualTo(3));
        Assert.That(snapshot[0].Message, Is.EqualTo("msg-1"));
        Assert.That(snapshot[1].Message, Is.EqualTo("msg-2"));
        Assert.That(snapshot[2].Message, Is.EqualTo("msg-3"));

        // Overwrite the ring buffer completely with more entries than capacity
        for (var i = 4; i <= 15; i++)
        {
            logger.Info($"msg-{i}");
        }

        // Original snapshot should remain intact and unchanged
        Assert.That(snapshot, Has.Count.EqualTo(3));
        Assert.That(snapshot[0].Message, Is.EqualTo("msg-1"));
        Assert.That(snapshot[1].Message, Is.EqualTo("msg-2"));
        Assert.That(snapshot[2].Message, Is.EqualTo("msg-3"));

        // A new call should return the latest overwritten entries in chronological order
        var updatedEntries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(updatedEntries, Has.Count.EqualTo(5));
        Assert.That(updatedEntries[0].Message, Is.EqualTo("msg-11"));
        Assert.That(updatedEntries[4].Message, Is.EqualTo("msg-15"));
    }

    [Test]
    public void Concurrent_writes_and_reads_should_be_thread_safe_and_stable()
    {
        var target = new RingBufferTarget(128);
        var logger = ConfigureAndGetLogger(target);

        const int writerCount = 4;
        const int writesPerThread = 250;
        const int readerCount = 4;
        using var cts = new CancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();

        var writerTasks = Enumerable.Range(0, writerCount).Select(writerIndex => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < writesPerThread; i++)
                {
                    logger.Info($"thread-{writerIndex}-msg-{i}");
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        var readerTasks = Enumerable.Range(0, readerCount).Select(_ => Task.Run(() =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var entries = target.GetEntries(50, LogLevel.Trace);
                    Assert.That(entries.Count, Is.LessThanOrEqualTo(50));

                    for (var i = 1; i < entries.Count; i++)
                    {
                        Assert.That(entries[i].Id, Is.GreaterThan(entries[i - 1].Id));
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        Task.WaitAll(writerTasks);
        cts.Cancel();
        Task.WaitAll(readerTasks);

        Assert.That(exceptions, Is.Empty);

        var finalEntries = target.GetEntries(128, LogLevel.Trace);
        Assert.That(finalEntries, Has.Count.EqualTo(128));
        for (var i = 1; i < finalEntries.Count; i++)
        {
            Assert.That(finalEntries[i].Id, Is.GreaterThan(finalEntries[i - 1].Id));
        }
    }

    [Test]
    public void GetEntries_should_filter_correctly_outside_lock()
    {
        var target = new RingBufferTarget(20);
        var logger = ConfigureAndGetLogger(target);

        logger.Trace("trace-msg");
        logger.Debug("debug-msg");
        logger.Info("info-msg-1");
        logger.Warn("warn-msg");
        logger.Error("error-msg");
        logger.Fatal("fatal-msg");
        logger.Info("info-msg-2");

        // When minimumLevel is null, return all entries
        var allEntries = target.GetEntries(10, null);
        Assert.That(allEntries, Has.Count.EqualTo(7));

        // Filter for Warn and higher (Warn, Error, Fatal)
        var warnAndHigher = target.GetEntries(10, LogLevel.Warn);
        Assert.That(warnAndHigher, Has.Count.EqualTo(3));
        Assert.That(warnAndHigher.Select(e => e.Level), Is.EqualTo(new[] { "Warn", "Error", "Fatal" }));

        // Filter with count limit smaller than matching count
        var recentInfo = target.GetEntries(1, LogLevel.Info);
        Assert.That(recentInfo, Has.Count.EqualTo(1));
        Assert.That(recentInfo[0].Message, Is.EqualTo("info-msg-2"));

        // Count <= 0 should return empty list
        Assert.That(target.GetEntries(0, LogLevel.Trace), Is.Empty);
        Assert.That(target.GetEntries(-5, LogLevel.Trace), Is.Empty);
    }

    [Test]
    public void Write_through_AsyncTargetWrapper_should_populate_RingBufferTarget()
    {
        var target = new RingBufferTarget(100);
        var asyncWrapper = new AsyncTargetWrapper(target, 5000, AsyncTargetWrapperOverflowAction.Discard)
        {
            Name = "ringBuffer"
        };

        var config = new LoggingConfiguration();
        config.AddTarget(asyncWrapper);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, asyncWrapper);
        LogManager.Configuration = config;
        var logger = LogManager.GetLogger("AsyncTestLogger");

        logger.Info("async message 1");
        logger.Info("async message 2");

        LogManager.Flush();

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].Message, Is.EqualTo("async message 1"));
        Assert.That(entries[1].Message, Is.EqualTo("async message 2"));
    }

    [Test]
    public void Write_should_redact_discord_webhook_tokens_in_message()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Info("Dispatched to https://discord.com/api/webhooks/123456789/secret-webhook-token");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Message, Is.EqualTo("Dispatched to https://discord.com/api/webhooks/123456789/[REDACTED]"));
        Assert.That(entries[0].Message, Does.Not.Contain("secret-webhook-token"));
    }

    [Test]
    public void Write_should_redact_telegram_bot_tokens_in_message()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Info("Posting to https://api.telegram.org/bot123456:ABC-DEF_987/sendMessage");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Message, Is.EqualTo("Posting to https://api.telegram.org/bot[REDACTED]/sendMessage"));
        Assert.That(entries[0].Message, Does.Not.Contain("123456:ABC-DEF_987"));
    }

    [Test]
    public void Write_should_redact_api_key_token_and_passkey_in_message()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        logger.Info("Connecting to https://indexer.com/api?apikey=my-secret-key-123&other=val with token=tok_abc and passkey=pass_xyz");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Message, Does.Not.Contain("my-secret-key-123"));
        Assert.That(entries[0].Message, Does.Not.Contain("tok_abc"));
        Assert.That(entries[0].Message, Does.Not.Contain("pass_xyz"));
        Assert.That(entries[0].Message, Does.Contain("apikey=[REDACTED]"));
        Assert.That(entries[0].Message, Does.Contain("token=[REDACTED]"));
        Assert.That(entries[0].Message, Does.Contain("passkey=[REDACTED]"));
    }

    [Test]
    public void Write_should_redact_sensitive_tokens_in_exception()
    {
        var target = new RingBufferTarget();
        var logger = ConfigureAndGetLogger(target);

        var innerEx = new InvalidOperationException("Failed calling https://api.site.com?api_key=secret-key-456 with token=tok_123");
        logger.Error(innerEx, "Request failed with passkey=topsecret");

        var entries = target.GetEntries(10, LogLevel.Trace);
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Message, Does.Not.Contain("topsecret"));
        Assert.That(entries[0].Message, Does.Contain("passkey=[REDACTED]"));
        Assert.That(entries[0].Exception, Does.Not.Contain("secret-key-456"));
        Assert.That(entries[0].Exception, Does.Not.Contain("tok_123"));
        Assert.That(entries[0].Exception, Does.Contain("api_key=[REDACTED]"));
        Assert.That(entries[0].Exception, Does.Contain("token=[REDACTED]"));
    }

    [Test]
    public void Sanitize_should_mask_discord_telegram_api_key_token_and_passkey_patterns()
    {
        Assert.That(
            RingBufferTarget.Sanitize("https://discord.com/api/webhooks/987654321/secret_token_123"),
            Is.EqualTo("https://discord.com/api/webhooks/987654321/[REDACTED]"));

        Assert.That(
            RingBufferTarget.Sanitize("https://api.telegram.org/bot123456:ABCdefGHI/sendMessage"),
            Is.EqualTo("https://api.telegram.org/bot[REDACTED]/sendMessage"));

        Assert.That(
            RingBufferTarget.Sanitize("https://api.telegram.org/botmySimpleToken/getMe"),
            Is.EqualTo("https://api.telegram.org/bot[REDACTED]/getMe"));

        Assert.That(
            RingBufferTarget.Sanitize("https://hooks.slack.com/services/T123/B456/7890"),
            Is.EqualTo("https://hooks.slack.com/services/[REDACTED]"));

        Assert.That(
            RingBufferTarget.Sanitize("api_key=secretKey123&passkey=pass123&token=tok123&apikey=key123"),
            Is.EqualTo("api_key=[REDACTED]&passkey=[REDACTED]&token=[REDACTED]&apikey=[REDACTED]"));

        Assert.That(
            RingBufferTarget.Sanitize("https://user:mypassword@example.com/api"),
            Is.EqualTo("https://user:[REDACTED]@example.com/api"));
    }

    [Test]
    public void Sanitize_should_handle_null_and_empty_strings()
    {
        Assert.That(RingBufferTarget.Sanitize(null), Is.Null);
        Assert.That(RingBufferTarget.Sanitize(string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Sanitize_should_be_idempotent_when_already_redacted()
    {
        const string discord = "https://discord.com/api/webhooks/123/[REDACTED]";
        const string telegram = "https://api.telegram.org/bot[REDACTED]/sendMessage";
        const string queryParams = "apikey=[REDACTED]&passkey=[REDACTED]&token=[REDACTED]";

        Assert.That(RingBufferTarget.Sanitize(discord), Is.EqualTo(discord));
        Assert.That(RingBufferTarget.Sanitize(telegram), Is.EqualTo(telegram));
        Assert.That(RingBufferTarget.Sanitize(queryParams), Is.EqualTo(queryParams));
    }
}
