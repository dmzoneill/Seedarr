using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;

namespace Seedarr.Http.Test.SignalR;

[TestFixture]
public class MessageHubSnapshotAndAuthTest
{
    private IConfigFileProvider _configFileProvider;
    private ITorrentService _torrentService;
    private HubCallerContext _callerContext;
    private IHubCallerClients _callerClients;
    private ISingleClientProxy _callerProxy;
    private DefaultHttpContext _httpContext;
    private FeatureCollection _features;

    [SetUp]
    public void SetUp()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _torrentService = Substitute.For<ITorrentService>();
        _callerContext = Substitute.For<HubCallerContext>();
        _callerClients = Substitute.For<IHubCallerClients>();
        _callerProxy = Substitute.For<ISingleClientProxy>();

        _callerContext.ConnectionId.Returns("conn-test-123");
        _callerClients.Caller.Returns(_callerProxy);
        _callerProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _httpContext = new DefaultHttpContext();
        _features = new FeatureCollection();
        var httpContextFeature = Substitute.For<IHttpContextFeature>();
        httpContextFeature.HttpContext.Returns(_httpContext);
        _features.Set<IHttpContextFeature>(httpContextFeature);
        _callerContext.Features.Returns(_features);

        MessageHub.ResetForTesting();
    }

    [TearDown]
    public void TearDown()
    {
        MessageHub.ResetForTesting();
    }

    [Test]
    public async Task RequestStateSnapshot_returns_consolidated_snapshot_structure()
    {
        var torrents = new List<Torrent>
        {
            new Torrent
            {
                Id = 1,
                Name = "Torrent 1",
                Status = TorrentStatus.Downloading,
                Progress = 42.5,
                DownloadSpeed = 1000000,
                UploadSpeed = 500000,
                Eta = 120,
                TotalSize = 5000000000,
                Active = true
            },
            new Torrent
            {
                Id = 2,
                Name = "Torrent 2",
                Status = TorrentStatus.Seeding,
                Progress = 100.0,
                DownloadSpeed = 0,
                UploadSpeed = 1500000,
                Eta = 0,
                TotalSize = 2000000000,
                Active = true
            },
            new Torrent
            {
                Id = 3,
                Name = "Torrent 3",
                Status = TorrentStatus.Paused,
                Progress = 10.0,
                DownloadSpeed = 0,
                UploadSpeed = 0,
                Eta = 0,
                TotalSize = 800000000,
                Active = false
            }
        };

        _torrentService.GetAll().Returns(torrents);

        var hub = new MessageHub(_configFileProvider, _torrentService)
        {
            Context = _callerContext,
            Clients = _callerClients
        };

        var snapshot = await hub.RequestStateSnapshot();

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.Torrents.Count, Is.EqualTo(3));
        Assert.That(snapshot.DownloadSpeed, Is.EqualTo(1000000));
        Assert.That(snapshot.UploadSpeed, Is.EqualTo(2000000));
        Assert.That(snapshot.ActiveCount, Is.EqualTo(2));
        Assert.That(snapshot.TotalCount, Is.EqualTo(3));
        Assert.That(snapshot.TimestampUtc, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));

        var first = snapshot.Torrents[0];
        Assert.That(first.Id, Is.EqualTo(1));
        Assert.That(first.Name, Is.EqualTo("Torrent 1"));
        Assert.That(first.Status, Is.EqualTo("Downloading"));
        Assert.That(first.Progress, Is.EqualTo(42.5));
        Assert.That(first.DownloadSpeed, Is.EqualTo(1000000));
        Assert.That(first.UploadSpeed, Is.EqualTo(500000));
        Assert.That(first.Eta, Is.EqualTo(120));
        Assert.That(first.Size, Is.EqualTo(5000000000));
        Assert.That(first.TotalSize, Is.EqualTo(5000000000));
        Assert.That(first.Active, Is.True);

        await _callerProxy.Received(1).SendCoreAsync("stateSnapshot", Arg.Is<object[]>(args => args.Length == 1 && args[0] == snapshot), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RequestStateSnapshot_handles_null_or_empty_torrents_gracefully()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        var hub = new MessageHub(_configFileProvider, _torrentService)
        {
            Context = _callerContext,
            Clients = _callerClients
        };

        var snapshot = await hub.RequestStateSnapshot();

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.Torrents, Is.Empty);
        Assert.That(snapshot.DownloadSpeed, Is.EqualTo(0));
        Assert.That(snapshot.UploadSpeed, Is.EqualTo(0));
        Assert.That(snapshot.ActiveCount, Is.EqualTo(0));
        Assert.That(snapshot.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task OnConnectedAsync_allows_connection_when_authentication_disabled()
    {
        _configFileProvider.AuthenticationEnabled.Returns(false);

        var hub = new MessageHub(_configFileProvider, _torrentService)
        {
            Context = _callerContext,
            Clients = _callerClients
        };

        await hub.OnConnectedAsync();

        _callerContext.DidNotReceive().Abort();
        Assert.That(MessageHub.IsConnected, Is.True);
        await _callerProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnConnectedAsync_allows_authenticated_cookie_or_session_user_when_AuthenticationEnabled_is_true()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "testuser"),
                new Claim(ClaimTypes.Role, "Admin")
            },
            "Cookies");
        var principal = new ClaimsPrincipal(identity);

        _callerContext.User.Returns(principal);

        var hub = new MessageHub(_configFileProvider, _torrentService)
        {
            Context = _callerContext,
            Clients = _callerClients
        };

        await hub.OnConnectedAsync();

        _callerContext.DidNotReceive().Abort();
        Assert.That(MessageHub.IsConnected, Is.True);
        await _callerProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnConnectedAsync_allows_connection_via_access_token_matching_masterApiKey()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("valid-master-api-key");

        _httpContext.Request.QueryString = new QueryString("?access_token=valid-master-api-key");

        var hub = new MessageHub(_configFileProvider, _torrentService)
        {
            Context = _callerContext,
            Clients = _callerClients
        };

        await hub.OnConnectedAsync();

        _callerContext.DidNotReceive().Abort();
        Assert.That(MessageHub.IsConnected, Is.True);
    }

    [Test]
    public async Task OnConnectedAsync_aborts_when_unauthenticated_and_access_token_mismatched()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("valid-master-api-key");

        _httpContext.Request.QueryString = new QueryString("?access_token=wrong-token");

        var hub = new MessageHub(_configFileProvider, _torrentService)
        {
            Context = _callerContext,
            Clients = _callerClients
        };

        await hub.OnConnectedAsync();

        _callerContext.Received(1).Abort();
        Assert.That(MessageHub.IsConnected, Is.False);
    }

    [Test]
    public async Task OnConnectedAsync_authenticates_via_registered_scheme_when_unauthenticated_on_Context()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "jwtuser") }, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Bearer");

        var authService = Substitute.For<IAuthenticationService>();
        authService.AuthenticateAsync(_httpContext, "Bearer")
            .Returns(Task.FromResult(AuthenticateResult.Success(ticket)));

        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        schemeProvider.GetAllSchemesAsync().Returns(Task.FromResult<IEnumerable<AuthenticationScheme>>(new[]
        {
            new AuthenticationScheme("Bearer", "Bearer", typeof(IAuthenticationHandler))
        }));

        var services = new ServiceCollection();
        services.AddSingleton(authService);
        services.AddSingleton(schemeProvider);
        _httpContext.RequestServices = services.BuildServiceProvider();

        _httpContext.Request.QueryString = new QueryString("?access_token=my-jwt-session-token");

        var hub = new MessageHub(_configFileProvider, _torrentService)
        {
            Context = _callerContext,
            Clients = _callerClients
        };

        await hub.OnConnectedAsync();

        _callerContext.DidNotReceive().Abort();
        Assert.That(MessageHub.IsConnected, Is.True);
        Assert.That(_httpContext.User?.Identity?.Name, Is.EqualTo("jwtuser"));
    }
}
