#pragma warning disable CA1849
#pragma warning disable IDE0007
#pragma warning disable SA1117

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Terminal;
using Seedarr.Http.Terminal;

namespace NzbDrone.Core.Test.Terminal;

[TestFixture]
public class TerminalServiceTests
{
    private MockPtyProcessFactory _mockFactory;
    private TerminalService _service;

    [SetUp]
    public void SetUp()
    {
        _mockFactory = new MockPtyProcessFactory();
        _service = new TerminalService(_mockFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    [Test]
    public async Task StartSessionAsync_should_create_session_and_track_in_ActiveSessionIds()
    {
        var connId = "conn-1";
        var session = await _service.StartSessionAsync(connId, 100, 40, _ => Task.CompletedTask);

        Assert.That(session, Is.Not.Null);
        Assert.That(session.ConnectionId, Is.EqualTo(connId));
        Assert.That(_service.HasSession(connId), Is.True);
        Assert.That(_service.ActiveSessionIds, Does.Contain(connId));
        Assert.That(_service.GetSession(connId), Is.SameAs(session));
        Assert.That(_mockFactory.LastCreatedProcess.LastCols, Is.EqualTo(100));
        Assert.That(_mockFactory.LastCreatedProcess.LastRows, Is.EqualTo(40));
    }

    [Test]
    public async Task StartSessionAsync_should_clamp_invalid_dimensions_to_defaults()
    {
        var connId = "conn-clamp";
        var session = await _service.StartSessionAsync(connId, 0, -10, _ => Task.CompletedTask);

        Assert.That(session, Is.Not.Null);
        Assert.That(_mockFactory.LastCreatedProcess.LastCols, Is.EqualTo(80));
        Assert.That(_mockFactory.LastCreatedProcess.LastRows, Is.EqualTo(24));
    }

    [Test]
    public async Task StartSessionAsync_when_session_already_exists_should_terminate_old_session()
    {
        var connId = "conn-replace";
        var session1 = await _service.StartSessionAsync(connId, 80, 24, _ => Task.CompletedTask);
        var proc1 = _mockFactory.LastCreatedProcess;

        var session2 = await _service.StartSessionAsync(connId, 120, 50, _ => Task.CompletedTask);
        var proc2 = _mockFactory.LastCreatedProcess;

        Assert.That(proc1.WasKilled, Is.True);
        Assert.That(session2, Is.Not.SameAs(session1));
        Assert.That(_service.GetSession(connId), Is.SameAs(session2));
        Assert.That(proc2.LastCols, Is.EqualTo(120));
    }

    [Test]
    public async Task WriteInputAsync_and_WriteInput_should_write_bytes_to_master_stream()
    {
        var connId = "conn-write";
        await _service.StartSessionAsync(connId, 80, 24, _ => Task.CompletedTask);
        var proc = _mockFactory.LastCreatedProcess;

        await _service.WriteInputAsync(connId, "ls -la\n");
        _service.WriteInput(connId, "pwd\n");

        var written = proc.Stream.GetWrittenString();
        Assert.That(written, Does.Contain("ls -la\n"));
        Assert.That(written, Does.Contain("pwd\n"));
    }

    [Test]
    public async Task Resize_should_clamp_and_propagate_dimensions_to_process()
    {
        var connId = "conn-resize";
        await _service.StartSessionAsync(connId, 80, 24, _ => Task.CompletedTask);
        var proc = _mockFactory.LastCreatedProcess;

        _service.Resize(connId, 140, 45);
        Assert.That(proc.LastCols, Is.EqualTo(140));
        Assert.That(proc.LastRows, Is.EqualTo(45));

        _service.Resize(connId, -5, 0);
        Assert.That(proc.LastCols, Is.EqualTo(80));
        Assert.That(proc.LastRows, Is.EqualTo(24));
    }

    [Test]
    public async Task CloseSessionAsync_should_kill_process_and_clean_up_session()
    {
        var connId = "conn-close";
        await _service.StartSessionAsync(connId, 80, 24, _ => Task.CompletedTask);
        var proc = _mockFactory.LastCreatedProcess;

        Assert.That(_service.HasSession(connId), Is.True);

        await _service.CloseSessionAsync(connId);

        Assert.That(_service.HasSession(connId), Is.False);
        Assert.That(_service.ActiveSessionIds, Does.Not.Contain(connId));
        Assert.That(proc.WasKilled, Is.True);
    }

    [Test]
    public async Task ReadLoop_should_stream_output_chunks_to_callback_and_trigger_onExit()
    {
        var connId = "conn-stream";
        var capturedOutput = new StringBuilder();
        var exitTcs = new TaskCompletionSource();

        await _service.StartSessionAsync(
            connId,
            80,
            24,
            output =>
            {
                capturedOutput.Append(output);
                return Task.CompletedTask;
            },
            () => exitTcs.TrySetResult());

        var proc = _mockFactory.LastCreatedProcess;

        proc.Stream.EnqueueOutput("Hello from PTY\n");
        proc.Stream.EnqueueOutput("Second line\n");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (capturedOutput.Length < "Hello from PTY\nSecond line\n".Length && sw.ElapsedMilliseconds < 3000)
        {
            await Task.Delay(50);
        }

        Assert.That(capturedOutput.ToString(), Does.Contain("Hello from PTY\n"));
        Assert.That(capturedOutput.ToString(), Does.Contain("Second line\n"));

        // Signal EOF from PTY master stream
        proc.Stream.CloseOutput();

        var completed = await Task.WhenAny(exitTcs.Task, Task.Delay(3000));
        Assert.That(completed, Is.EqualTo(exitTcs.Task));
        Assert.That(_service.HasSession(connId), Is.False);
    }

    [Test]
    public async Task ApplicationShutdownRequested_should_terminate_all_active_sessions()
    {
        await _service.StartSessionAsync("c1", 80, 24, _ => Task.CompletedTask);
        var p1 = _mockFactory.LastCreatedProcess;

        await _service.StartSessionAsync("c2", 80, 24, _ => Task.CompletedTask);
        var p2 = _mockFactory.LastCreatedProcess;

        Assert.That(_service.ActiveSessionIds.Count, Is.EqualTo(2));

        _service.Handle(new ApplicationShutdownRequested());

        Assert.That(_service.ActiveSessionIds.Count, Is.EqualTo(0));
        Assert.That(p1.WasKilled, Is.True);
        Assert.That(p2.WasKilled, Is.True);
    }

    [Test]
    public async Task TerminalHub_StartSession_WriteInput_Resize_and_OnDisconnectedAsync_work_correctly()
    {
        var mockService = Substitute.For<ITerminalService>();
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.TerminalAccessEnabled.Returns(true);
        configFileProvider.AuthenticationEnabled.Returns(false);

        var hub = new TerminalHub(mockService, configFileProvider);
        var callerClients = Substitute.For<IHubCallerClients>();
        var clientCaller = Substitute.For<ISingleClientProxy>();
        callerClients.Caller.Returns(clientCaller);

        var hubContext = Substitute.For<HubCallerContext>();
        hubContext.ConnectionId.Returns("hub-conn-1");
        hubContext.ConnectionAborted.Returns(CancellationToken.None);

        hub.Context = hubContext;
        hub.Clients = callerClients;

        await hub.StartSession(100, 30);
        await mockService.Received(1).StartSessionAsync(
            "hub-conn-1",
            100,
            30,
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Action>(),
            Arg.Any<CancellationToken>());

        await hub.WriteInput("echo hi\n");
        await mockService.Received(1).WriteInputAsync("hub-conn-1", "echo hi\n");

        hub.Resize(110, 35);
        mockService.Received(1).Resize("hub-conn-1", 110, 35);

        await hub.OnDisconnectedAsync(null);
        await mockService.Received(1).CloseSessionAsync("hub-conn-1");
    }

    [Test]
    public async Task PosixPtyProcess_Start_and_execution_on_posix_systems()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("POSIX PTY test is only applicable on Linux/macOS");
            return;
        }

        using var process = PosixPtyProcess.Start(
            80,
            24,
            "/bin/sh");

        Assert.That(process, Is.Not.Null);
        Assert.That(process.Pid, Is.GreaterThan(0));

        var input = Encoding.UTF8.GetBytes("echo PTY_TEST_OK\nexit\n");
        await process.MasterStream.WriteAsync(input.AsMemory());

        var buffer = new byte[1024];
        var totalOutput = new StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < 4000)
        {
            var read = await process.MasterStream.ReadAsync(buffer.AsMemory());
            if (read <= 0)
            {
                break;
            }

            totalOutput.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (totalOutput.ToString().Contains("PTY_TEST_OK"))
            {
                break;
            }
        }

        Assert.That(totalOutput.ToString(), Does.Contain("PTY_TEST_OK"));

        process.Resize(100, 30);
        process.Kill();
        Assert.That(process.HasExited, Is.True);
    }

    private class MockPtyProcessFactory : IPtyProcessFactory
    {
        public MockPtyProcess LastCreatedProcess { get; private set; }

        public IPtyProcess Create(
            int cols = 80,
            int rows = 24,
            string command = null,
            string[] args = null,
            string workingDirectory = null,
            IDictionary<string, string> environment = null)
        {
            LastCreatedProcess = new MockPtyProcess(cols, rows);
            return LastCreatedProcess;
        }
    }

    private class MockPtyProcess : IPtyProcess
    {
        public MockPtyProcess(int cols, int rows)
        {
            LastCols = cols;
            LastRows = rows;
            Stream = new MockDuplexStream();
        }

        public int Pid { get; set; } = 12345;
        public bool HasExited { get; set; }
        public int ExitCode { get; set; }
        public int LastCols { get; private set; }
        public int LastRows { get; private set; }
        public bool WasKilled { get; private set; }
        public bool WasDisposed { get; private set; }
        public MockDuplexStream Stream { get; }
        public Stream MasterStream => Stream;

        public void Resize(int cols, int rows)
        {
            LastCols = cols;
            LastRows = rows;
        }

        public void Kill()
        {
            WasKilled = true;
            HasExited = true;
            Stream.CloseOutput();
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            WasDisposed = true;
            Stream?.Dispose();
        }
    }

    private class MockDuplexStream : Stream
    {
        private readonly MemoryStream _writtenBytes = new();
        private readonly ConcurrentQueue<byte[]> _pendingOutput = new();
        private readonly SemaphoreSlim _outputSignal = new(0);
        private bool _isClosed;
        private byte[] _currentChunk;
        private int _currentChunkOffset;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void EnqueueOutput(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _pendingOutput.Enqueue(bytes);
            _outputSignal.Release();
        }

        public void CloseOutput()
        {
            _isClosed = true;
            _outputSignal.Release();
        }

        public string GetWrittenString()
        {
            lock (_writtenBytes)
            {
                return Encoding.UTF8.GetString(_writtenBytes.ToArray());
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                if (_currentChunk != null && _currentChunkOffset < _currentChunk.Length)
                {
                    var available = _currentChunk.Length - _currentChunkOffset;
                    var toCopy = Math.Min(available, buffer.Length);
                    _currentChunk.AsSpan(_currentChunkOffset, toCopy).CopyTo(buffer.Span);
                    _currentChunkOffset += toCopy;
                    if (_currentChunkOffset >= _currentChunk.Length)
                    {
                        _currentChunk = null;
                        _currentChunkOffset = 0;
                    }

                    return toCopy;
                }

                if (_pendingOutput.TryDequeue(out var nextChunk))
                {
                    _currentChunk = nextChunk;
                    _currentChunkOffset = 0;
                    continue;
                }

                if (_isClosed)
                {
                    return 0;
                }

                try
                {
                    await _outputSignal.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return 0;
                }
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_writtenBytes)
            {
                _writtenBytes.Write(buffer, offset, count);
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                lock (_writtenBytes)
                {
                    _writtenBytes.Write(buffer.Span);
                }
            }, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        protected override void Dispose(bool disposing)
        {
            CloseOutput();
            _outputSignal.Dispose();
            _writtenBytes.Dispose();
            base.Dispose(disposing);
        }
    }
}
