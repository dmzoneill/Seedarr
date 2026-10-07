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
public class TerminalWebSocketCreateSessionErrorTest
{
    private const string SensitiveOracle = "SENSITIVE_ORACLE_967_C:\\internal\\path";

    [Test]
    public async Task HandleWebSocket_when_CreateSession_throws_does_not_echo_exception_message()
    {
        var ptyService = Substitute.For<IPtyTerminalService>();
        ptyService
            .CreateSession(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(_ => throw new InvalidOperationException(SensitiveOracle));

        var configService = Substitute.For<IConfigService>();
        configService.TorrentSaveDirectory.Returns("/tmp");

        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.AuthenticationEnabled.Returns(false);
        configFileProvider.TerminalAccessEnabled.Returns(true);

        var socketFeature = new TestHttpWebSocketFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(socketFeature);

        await TerminalWebSocketHandler.HandleWebSocket(context, ptyService, configService, configFileProvider);

        var recordingSocket = socketFeature.Socket;
        Assert.That(recordingSocket, Is.Not.Null);

        var combined = string.Join("\n", recordingSocket.SentTextPayloads);
        Assert.That(combined, Does.Not.Contain(SensitiveOracle));
        Assert.That(combined, Does.Contain("could not be started"));
        Assert.That(recordingSocket.CloseDescription, Is.EqualTo("Terminal session failed"));
        Assert.That(recordingSocket.CloseDescription, Does.Not.Contain("internal"));
    }

    private sealed class TestHttpWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public RecordingWebSocket Socket { get; private set; }

        public Task<WebSocket> AcceptWebSocketAsync(WebSocketAcceptContext acceptContext)
        {
            Socket = new RecordingWebSocket();
            return Task.FromResult<WebSocket>(Socket);
        }
    }

    private sealed class RecordingWebSocket : WebSocket
    {
        private readonly List<string> _sentTextPayloads = new();

        public IReadOnlyList<string> SentTextPayloads => _sentTextPayloads;

        public string CloseDescription { get; private set; }

        public override WebSocketCloseStatus? CloseStatus { get; }

        public override string SubProtocol => null;

        public override WebSocketState State { get; private set; } = WebSocketState.Open;

        public override void Abort()
        {
            State = WebSocketState.Aborted;
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            CloseDescription = statusDescription;
            State = WebSocketState.Closed;
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
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (messageType == WebSocketMessageType.Text)
            {
                _sentTextPayloads.Add(Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count));
            }

            return Task.CompletedTask;
        }
    }
}
