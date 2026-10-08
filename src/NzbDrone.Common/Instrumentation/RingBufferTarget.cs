using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NLog.Targets;

namespace NzbDrone.Common.Instrumentation;

[Target("RingBuffer")]
public class RingBufferTarget : TargetWithLayout
{
    private static readonly Regex TelegramBotPathRegex = new(
        @"(api\.telegram\.org/bot)[^/?#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TelegramBotTokenRegex = new(
        @"\bbot\d+:[A-Za-z0-9_-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DiscordWebhookRegex = new(
        @"/api/webhooks/(?<id>\d+)/[^/?#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SlackWebhookRegex = new(
        @"/services/T[A-Za-z0-9]+/B[A-Za-z0-9]+/[A-Za-z0-9]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SensitiveParamRegex = new(
        @"((?:^|[?&,\s""';]|\b(?:and|with)\s+)(?:api[_-]?key|bot[_-]?token|token|passkey|secret|password|access[_-]?token|client[_-]?secret|refresh[_-]?token|auth)=)[^&\s""';]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StandalonePasskeyRegex = new(
        @"\bpasskey=[^\s&""';]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StandaloneTokenRegex = new(
        @"\btoken=[^\s&""';]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SensitiveJsonRegex = new(
        "(\"?(?:api[_-]?key|passkey|password|token|secret|access[_-]?token|client[_-]?secret|refresh[_-]?token|bot[_-]?token|auth|key)\"?\\s*[:=]\\s*\"?)(?:\"(?:[^\"\\\\]|\\\\.)*\"|[^\"',&}]+)(\"?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BearerTokenRegex = new(
        @"\bBearer\s+[A-Za-z0-9_\-\.]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AuthorizationHeaderCredentialRegex = new(
        @"(Authorization:\s*)(Basic|ApiKey|Token)\s+\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BasicAuthRegex = new(
        @"(https?://[^:/@\s]+:)([^@/\s]+)(@)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrackerPasskeyPathRegex = new(
        @"(?i)(/passkey/)[^/?#\s]+",
        RegexOptions.Compiled);

    private static readonly Regex TrackerPathTokenBeforeAnnounceRegex = new(
        @"(/)(?!announce|scrape)[A-Za-z0-9_-]{16,64}(/announce)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrackerPathTokenAfterAnnounceRegex = new(
        @"(?i)(/announce/)[A-Za-z0-9_-]{16,64}",
        RegexOptions.Compiled);

    private readonly object _lock = new();
    private readonly LogEntryRecord[] _buffer;
    private int _position;
    private int _count;
    private int _currentId;

    public int Capacity { get; }

    public RingBufferTarget(int capacity = 2048)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
        }

        Capacity = capacity;
        _buffer = new LogEntryRecord[capacity];
    }

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = TelegramBotPathRegex.Replace(input, "$1[REDACTED]");
        result = TelegramBotTokenRegex.Replace(result, "bot[REDACTED]");
        result = DiscordWebhookRegex.Replace(result, "/api/webhooks/${id}/[REDACTED]");
        result = SlackWebhookRegex.Replace(result, "/services/[REDACTED]");
        result = SensitiveParamRegex.Replace(result, "$1[REDACTED]");
        result = StandalonePasskeyRegex.Replace(result, "passkey=[REDACTED]");
        result = StandaloneTokenRegex.Replace(result, "token=[REDACTED]");
        result = SensitiveJsonRegex.Replace(result, "$1[REDACTED]$2");
        result = BearerTokenRegex.Replace(result, "Bearer [REDACTED]");
        result = AuthorizationHeaderCredentialRegex.Replace(result, "${1}${2} [REDACTED]");
        result = BasicAuthRegex.Replace(result, "$1[REDACTED]$3");
        result = TrackerPasskeyPathRegex.Replace(result, "$1[REDACTED]");
        result = TrackerPathTokenBeforeAnnounceRegex.Replace(result, "$1[REDACTED]$2");
        result = TrackerPathTokenAfterAnnounceRegex.Replace(result, "$1[REDACTED]");

        return result;
    }

    protected override void Write(LogEventInfo logEvent)
    {
        var entry = new LogEntryRecord
        {
            Time = logEvent.TimeStamp.ToUniversalTime(),
            Level = logEvent.Level.Name,
            Logger = logEvent.LoggerName,
            Message = Sanitize(logEvent.FormattedMessage),
            Exception = Sanitize(logEvent.Exception?.ToString())
        };

        lock (_lock)
        {
            entry.Id = ++_currentId;
            _buffer[_position] = entry;
            _position = (_position + 1) % Capacity;

            if (_count < Capacity)
            {
                _count++;
            }
        }
    }

    public List<LogEntryRecord> GetEntries(int count, LogLevel minimumLevel)
    {
        if (count <= 0)
        {
            return new List<LogEntryRecord>();
        }

        LogEntryRecord[] snapshot;

        lock (_lock)
        {
            if (_count == 0)
            {
                return new List<LogEntryRecord>();
            }

            snapshot = new LogEntryRecord[_count];

            if (_count < Capacity)
            {
                Array.Copy(_buffer, 0, snapshot, 0, _count);
            }
            else
            {
                var rightLength = Capacity - _position;
                Array.Copy(_buffer, _position, snapshot, 0, rightLength);

                if (_position > 0)
                {
                    Array.Copy(_buffer, 0, snapshot, rightLength, _position);
                }
            }
        }

        var result = new List<LogEntryRecord>(Math.Min(snapshot.Length, count));

        for (var i = 0; i < snapshot.Length; i++)
        {
            var entry = snapshot[i];

            if (entry == null)
            {
                continue;
            }

            if (!MeetsMinimumLevel(entry.Level, minimumLevel))
            {
                continue;
            }

            result.Add(entry);
        }

        // Take the last 'count' entries (most recent)
        if (result.Count > count)
        {
            result = result.Skip(result.Count - count).ToList();
        }

        return result;
    }

    private static bool MeetsMinimumLevel(string level, LogLevel minimumLevel)
    {
        if (minimumLevel == null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(level))
        {
            return minimumLevel <= LogLevel.Trace;
        }

        if (!IsStandardLogLevelName(level))
        {
            return minimumLevel <= LogLevel.Trace;
        }

        try
        {
            return LogLevel.FromString(level) >= minimumLevel;
        }
        catch (ArgumentException)
        {
            return minimumLevel <= LogLevel.Trace;
        }
    }

    private static bool IsStandardLogLevelName(string level)
    {
        return level.Equals("Trace", StringComparison.OrdinalIgnoreCase)
               || level.Equals("Debug", StringComparison.OrdinalIgnoreCase)
               || level.Equals("Info", StringComparison.OrdinalIgnoreCase)
               || level.Equals("Warn", StringComparison.OrdinalIgnoreCase)
               || level.Equals("Error", StringComparison.OrdinalIgnoreCase)
               || level.Equals("Fatal", StringComparison.OrdinalIgnoreCase);
    }

    public static RingBufferTarget Instance { get; set; }
}
