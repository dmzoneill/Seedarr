// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class TerminalWebSocketJsonFallbackTest
{
    [TestCase("{\"type\":\"unknown\",\"data\":\"x\"}")]
    [TestCase("{\"data\":\"ls\\n\"}")]
    [TestCase("{\"type\":\"input\"}")]
    public async Task HandleWebSocket_forwards_unhandled_json_objects_as_raw_bytes(string frame)
    {
        var written = await RunSingleInboundFrameAsync(frame);

        Assert.That(written, Has.Count.EqualTo(1));
        Assert.That(written[0], Is.EqualTo(Encoding.UTF8.GetBytes(frame)));
    }

    [Test]
    public async Task HandleWebSocket_writes_input_payload_for_valid_input_message()
    {
        const string frame = "{\"type\":\"input\",\"data\":\"hi\"}";
        var written = await RunSingleInboundFrameAsync(frame);

        Assert.That(written, Has.Count.EqualTo(1));
        Assert.That(written[0], Is.EqualTo(Encoding.UTF8.GetBytes("hi")));
    }

    private static async Task<List<byte[]>> RunSingleInboundFrameAsync(string inboundFrame)
    {
        var written = new List<byte[]>();
        var session = Substitute.For<ITerminalSession>();
        session.ProcessId.Returns(1);
        session.IsActive.Returns(true);
        session.DisposeAsync().Returns(ValueTask.CompletedTask);
        session.ReadAsync(Arg.Any<Memory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult(0));
        session.WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                written.Add(callInfo.Arg<ReadOnlyMemory<byte>>().ToArray());
                return ValueTask.CompletedTask;
            });

        var ptyService = Substitute.For<IPtyTerminalService>();
        ptyService.CreateSession(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>()).Returns(session);

        var configService = Substitute.For<IConfigService>();
        configService.TorrentSaveDirectory.Returns("/tmp");

        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.AuthenticationEnabled.Returns(false);
        configFileProvider.TerminalAccessEnabled.Returns(true);

        var socketFeature = new TestHttpWebSocketFeature(Encoding.UTF8.GetBytes(inboundFrame));
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(socketFeature);

        await TerminalWebSocketHandler.HandleWebSocket(context, ptyService, configService, configFileProvider);

        return written;
    }

    private sealed class TestHttpWebSocketFeature : IHttpWebSocketFeature
    {
        private readonly byte[] _inboundFrame;

        public TestHttpWebSocketFeature(byte[] inboundFrame)
        {
            _inboundFrame = inboundFrame;
        }

        public bool IsWebSocketRequest => true;

        public QueuedWebSocket Socket { get; private set; }

        public Task<WebSocket> AcceptWebSocketAsync(WebSocketAcceptContext acceptContext) => AcceptAsync(acceptContext);

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext acceptContext)
        {
            Socket = new QueuedWebSocket(_inboundFrame);
            return Task.FromResult<WebSocket>(Socket);
        }
    }

    private sealed class QueuedWebSocket : WebSocket
    {
        private readonly Queue<byte[]> _inbound = new();
        private bool _sentClose;

        public QueuedWebSocket(byte[] inboundFrame)
        {
            _inbound.Enqueue(inboundFrame);
        }

        public override WebSocketCloseStatus? CloseStatus { get; }

        public override string CloseStatusDescription => null;

        public override string SubProtocol => null;

        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketState State => _state;

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_inbound.Count > 0)
            {
                var payload = _inbound.Dequeue();
                var count = Math.Min(payload.Length, buffer.Count);
                Buffer.BlockCopy(payload, 0, buffer.Array!, buffer.Offset, count);
                return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, true));
            }

            if (!_sentClose)
            {
                _sentClose = true;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            _state = WebSocketState.Closed;
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
