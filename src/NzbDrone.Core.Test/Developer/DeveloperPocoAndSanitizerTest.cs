// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using NzbDrone.Core.Developer.Debugger;
using NzbDrone.Core.Developer.GitHub;
using NzbDrone.Core.Developer.Quality;
using NzbDrone.Core.Developer.Repl;
using NzbDrone.Core.Developer.Uml;
using Seedarr.Http.Terminal;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperPocoAndSanitizerTest
{
    [Test]
    public void TerminalEnvironmentSanitizer_identifies_sensitive_and_safe_keys()
    {
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey("API_KEY"), Is.True);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey("PASSWORD"), Is.True);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey("DATABASE_URL"), Is.True);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey("SEEDARR_SECRET"), Is.True);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey("JWT_SECRET"), Is.True);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey("AUTH_TOKEN"), Is.True);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey(null), Is.False);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey(""), Is.False);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey("PATH"), Is.False);
        Assert.That(TerminalEnvironmentSanitizer.IsSensitiveKey("TERM"), Is.False);
    }

    [Test]
    public void TerminalEnvironmentSanitizer_strips_sensitive_keys_from_process_start_info()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["SEEDARR_API_KEY"] = "secret123";
        psi.Environment["DATABASE_PASSWORD"] = "pass456";
        psi.Environment["SAFE_VAR"] = "visible";

        TerminalEnvironmentSanitizer.StripSensitiveKeys(psi);

        Assert.That(psi.Environment.ContainsKey("SEEDARR_API_KEY"), Is.False);
        Assert.That(psi.Environment.ContainsKey("DATABASE_PASSWORD"), Is.False);
        Assert.That(psi.Environment.ContainsKey("SAFE_VAR"), Is.True);
    }

    [Test]
    public void DebuggerModels_verify_default_properties_and_initializers()
    {
        var tracepoint = new TracepointDefinition
        {
            FilePath = "test.cs",
            LineNumber = 42,
            Condition = "x > 0",
            HitCount = 5,
            IsEnabled = true,
        };
        Assert.That(tracepoint.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(tracepoint.CreatedAtUtc, Is.LessThanOrEqualTo(DateTime.UtcNow));

        var snapshot = new TracepointSnapshot
        {
            TracepointId = "tp1",
            FilePath = "test.cs",
            LineNumber = 42,
            ThreadId = 1,
            CallStack = "stack",
            VariablesJson = "{\"a\": 1}",
        };
        Assert.That(snapshot.SnapshotId, Is.Not.Null.And.Not.Empty);
        Assert.That(snapshot.TimestampUtc, Is.LessThanOrEqualTo(DateTime.UtcNow));

        var status = new DebuggerStatusReport
        {
            IsDapAvailable = true,
            DapPath = "/usr/bin/netcoredbg",
            AttachedSessionCount = 2,
            ActiveTracepointsCount = 3,
            CapturedSnapshotsCount = 10,
        };
        Assert.That(status.IsDapAvailable, Is.True);
        Assert.That(status.ServerTimestampUtc, Is.LessThanOrEqualTo(DateTime.UtcNow));

        var sourceItem = new DebuggerSourceFileItem
        {
            FilePath = "src/Test.cs",
            ClassName = "Test",
            Namespace = "App",
            Subsystem = "Core",
            LineCount = 100,
        };
        Assert.That(sourceItem.LineCount, Is.EqualTo(100));

        var sourceCode = new DebuggerSourceCodeResponse
        {
            FilePath = "src/Test.cs",
            Content = "console.log('test');",
            LineCount = 1,
            Exists = true,
        };
        Assert.That(sourceCode.LineCount, Is.EqualTo(1));
        Assert.That(sourceCode.Exists, Is.True);
    }

    [Test]
    public void ReplModels_verify_properties_and_initializers()
    {
        var req = new ReplExecutionRequest
        {
            Code = "1 + 1",
            Language = "javascript",
            TimeoutSeconds = 5,
        };
        Assert.That(req.Code, Is.EqualTo("1 + 1"));

        var resp = new ReplExecutionResponse
        {
            Success = true,
            ResultType = "number",
            ResultJson = "2",
            Output = "2",
            ErrorMessage = null,
            DurationMs = 12,
        };
        Assert.That(resp.Success, Is.True);
        Assert.That(resp.DurationMs, Is.EqualTo(12));

        var hist = new ReplHistoryEntry
        {
            Code = "2 + 2",
            Language = "js",
            Success = true,
            DurationMs = 10,
        };
        Assert.That(hist.ExecutedAtUtc, Is.LessThanOrEqualTo(DateTime.UtcNow));
    }

    [Test]
    public void UmlAndQualityModels_verify_properties()
    {
        var opt = new DeveloperUmlOptions
        {
            DiagramType = "di",
            Subsystem = "torrents",
            IncludeInterfaces = false,
            IncludeMethods = false,
        };
        Assert.That(opt.DiagramType, Is.EqualTo("di"));

        var qGate = new QualityGateStatus
        {
            Status = "ERROR",
            Conditions = new List<QualityGateMetric>
            {
                new() { MetricKey = "new_coverage", Status = "ERROR", ActualValue = "79.6", ErrorThreshold = "80", Comparator = "LT" },
            },
        };
        Assert.That(qGate.Conditions.Count, Is.EqualTo(1));

        var qMetrics = new QualityOverviewMetrics
        {
            Coverage = 75.0,
            NewCodeCoverage = 79.6,
            DuplicationDensity = 2.0,
            Bugs = 0,
            Vulnerabilities = 0,
            CodeSmells = 0,
            SecurityHotspotsReviewed = 100.0,
            ReliabilityRating = "A",
            SecurityRating = "A",
            MaintainabilityRating = "A",
            TechnicalDebtFormatted = "0 min",
        };
        Assert.That(qMetrics.NewCodeCoverage, Is.EqualTo(79.6));

        var ghPr = new DeveloperPullRequestItem
        {
            Number = 42,
            Title = "PR 42",
            State = "open",
            Author = "author",
            HtmlUrl = "https://github.com",
            CreatedAt = DateTime.UtcNow,
            Labels = new List<string> { "enhancement" },
            Draft = false,
        };
        Assert.That(ghPr.Number, Is.EqualTo(42));
    }
}
