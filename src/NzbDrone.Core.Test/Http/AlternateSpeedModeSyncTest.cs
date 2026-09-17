using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Deluge;
using Seedarr.Api.V1.QBittorrent;
using Seedarr.Api.V1.Transmission;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class AlternateSpeedModeSyncTest
{
    private IConfigService _configService;
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITorrentFileParser _torrentFileParser;
    private ITorrentImportService _torrentImportService;
    private ITrackerEntryService _trackerEntryService;
    private ITagService _tagService;
    private IConfigFileProvider _configFileProvider;
    private ICategoryService _categoryService;

    private QBittorrentApiController _qbittorrentController;
    private TransmissionRpcController _transmissionController;
    private DelugeJsonRpcController _delugeController;

    private bool _altEnabled;
    private int _maxDownloadSpeedKbps;
    private int _maxUploadSpeedKbps;
    private int _altDownloadSpeedKbps;
    private int _altUploadSpeedKbps;

    [SetUp]
    public void SetUp()
    {
        _altEnabled = false;
        _maxDownloadSpeedKbps = 1250;
        _maxUploadSpeedKbps = 625;
        _altDownloadSpeedKbps = 100;
        _altUploadSpeedKbps = 50;

        _configService = Substitute.For<IConfigService>();
        _configService.AlternativeSpeedEnabled.Returns(_ => _altEnabled);
        _configService.MaxDownloadSpeedKbps.Returns(_ => _maxDownloadSpeedKbps);
        _configService.MaxUploadSpeedKbps.Returns(_ => _maxUploadSpeedKbps);
        _configService.AltDownloadSpeedKbps.Returns(_ => _altDownloadSpeedKbps);
        _configService.AltUploadSpeedKbps.Returns(_ => _altUploadSpeedKbps);

        _configService.When(c => c.SaveConfigDictionary(Arg.Any<Dictionary<string, object>>()))
            .Do(callInfo =>
            {
                var dict = callInfo.Arg<Dictionary<string, object>>();
                if (dict.TryGetValue("AlternativeSpeedEnabled", out var alt))
                {
                    _altEnabled = Convert.ToBoolean(alt);
                }

                if (dict.TryGetValue("MaxDownloadSpeedKbps", out var maxDl))
                {
                    _maxDownloadSpeedKbps = Convert.ToInt32(maxDl);
                }

                if (dict.TryGetValue("MaxUploadSpeedKbps", out var maxUl))
                {
                    _maxUploadSpeedKbps = Convert.ToInt32(maxUl);
                }

                if (dict.TryGetValue("AltDownloadSpeedKbps", out var altDl))
                {
                    _altDownloadSpeedKbps = Convert.ToInt32(altDl);
                }

                if (dict.TryGetValue("AltUploadSpeedKbps", out var altUl))
                {
                    _altUploadSpeedKbps = Convert.ToInt32(altUl);
                }
            });

        _torrentService = Substitute.For<ITorrentService>();
        _torrentService.GetAll().Returns(new List<Torrent>());
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _tagService = Substitute.For<ITagService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.AuthenticationEnabled.Returns(false);
        _categoryService = Substitute.For<ICategoryService>();

        _qbittorrentController = new QBittorrentApiController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _tagService,
            _configFileProvider,
            categoryService: _categoryService);

        _transmissionController = new TransmissionRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _trackerEntryService,
            _configService,
            _configFileProvider,
            categoryService: _categoryService);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Transmission-Session-Id"] = TransmissionRpcController.CurrentSessionId;
        _transmissionController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        _delugeController = new DelugeJsonRpcController(
            _torrentService,
            _torrentFileService,
            _torrentFileParser,
            _torrentImportService,
            _configService,
            _tagService,
            _configFileProvider,
            categoryService: _categoryService);
    }

    [Test]
    public async Task Toggling_AlternateSpeedMode_In_QBittorrent_Reflects_In_Transmission_And_Deluge()
    {
        // 1. Initial state: normal mode
        Assert.That(_qbittorrentController.GetSpeedLimitsMode().Value, Is.EqualTo(0));

        // 2. Toggle in qBittorrent
        _qbittorrentController.ToggleSpeedLimitsMode();
        Assert.That(_altEnabled, Is.True);
        Assert.That(_qbittorrentController.GetSpeedLimitsMode().Value, Is.EqualTo(1));

        // 3. Check Transmission session-get
        var txReq = new TransmissionRpcRequest { Method = "session-get" };
        var txRes = await _transmissionController.HandleRpc(txReq);
        var txOk = txRes as OkObjectResult;
        Assert.That(txOk, Is.Not.Null);
        var txResponse = txOk.Value as TransmissionRpcResponse;
        var txArgs = txResponse?.Arguments as Dictionary<string, object>;
        Assert.That(txArgs, Is.Not.Null);
        Assert.That(txArgs["alt-speed-enabled"], Is.True);

        // 4. Check Deluge core.get_config and web.update_ui
        var delugeConfigJson = "{\"method\": \"core.get_config\", \"params\": [], \"id\": 1}";
        using var configDoc = JsonDocument.Parse(delugeConfigJson);
        var delugeRes = await _delugeController.HandleRpc(configDoc.RootElement);
        var jsonRes = delugeRes as JsonResult;
        Assert.That(jsonRes, Is.Not.Null);
        var serialized = JsonSerializer.Serialize(jsonRes.Value);
        using var resDoc = JsonDocument.Parse(serialized);
        var configObj = resDoc.RootElement.GetProperty("result");
        Assert.That(configObj.GetProperty("alt_speed_enabled").GetBoolean(), Is.True);
        Assert.That(configObj.GetProperty("max_download_speed").GetDouble(), Is.EqualTo(100.0));
        Assert.That(configObj.GetProperty("max_upload_speed").GetDouble(), Is.EqualTo(50.0));
    }

    [Test]
    public async Task Toggling_AlternateSpeedMode_In_Transmission_Reflects_In_QBittorrent_And_Deluge()
    {
        // 1. Enable via Transmission session-set
        var setReq = new TransmissionRpcRequest
        {
            Method = "session-set",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["alt-speed-enabled"] = JsonDocument.Parse("true").RootElement,
            },
        };
        await _transmissionController.HandleRpc(setReq);
        Assert.That(_altEnabled, Is.True);

        // 2. Check qBittorrent transfer/info
        var qbTransfer = _qbittorrentController.GetTransferInfo();
        var qbOk = qbTransfer.Result as OkObjectResult;
        Assert.That(qbOk, Is.Not.Null);
        var qbDict = qbOk.Value as Dictionary<string, object>;
        Assert.That(qbDict, Is.Not.Null);
        Assert.That(qbDict["dl_rate_limit"], Is.EqualTo(100 * 1024));
        Assert.That(qbDict["up_rate_limit"], Is.EqualTo(50 * 1024));

        // 3. Check Deluge web.update_ui
        var delugeUiJson = "{\"method\": \"web.update_ui\", \"params\": [[], {}], \"id\": 2}";
        using var uiDoc = JsonDocument.Parse(delugeUiJson);
        var delugeUiRes = await _delugeController.HandleRpc(uiDoc.RootElement);
        var uiJsonRes = delugeUiRes as JsonResult;
        Assert.That(uiJsonRes, Is.Not.Null);
        var serializedUi = JsonSerializer.Serialize(uiJsonRes.Value);
        using var uiResDoc = JsonDocument.Parse(serializedUi);
        var stats = uiResDoc.RootElement.GetProperty("result").GetProperty("stats");
        Assert.That(stats.GetProperty("max_download").GetDouble(), Is.EqualTo(100.0));
        Assert.That(stats.GetProperty("max_upload").GetDouble(), Is.EqualTo(50.0));
    }

    [Test]
    public async Task Toggling_AlternateSpeedMode_In_Deluge_Reflects_In_QBittorrent_And_Transmission()
    {
        // 1. Enable via Deluge core.set_config
        var delugeSetConfigJson = "{\"method\": \"core.set_config\", \"params\": [{\"alt_speed_enabled\": true}], \"id\": 3}";
        using var setDoc = JsonDocument.Parse(delugeSetConfigJson);
        await _delugeController.HandleRpc(setDoc.RootElement);
        Assert.That(_altEnabled, Is.True);

        // 2. Check qBittorrent speedLimitsMode
        var qbMode = _qbittorrentController.GetSpeedLimitsMode();
        Assert.That(qbMode.Value, Is.EqualTo(1));

        // 3. Check Transmission session-get
        var txReq = new TransmissionRpcRequest { Method = "session-get" };
        var txRes = await _transmissionController.HandleRpc(txReq);
        var txOk = txRes as OkObjectResult;
        var txResponse = txOk?.Value as TransmissionRpcResponse;
        var txArgs = txResponse?.Arguments as Dictionary<string, object>;
        Assert.That(txArgs?["alt-speed-enabled"], Is.True);
    }
}
