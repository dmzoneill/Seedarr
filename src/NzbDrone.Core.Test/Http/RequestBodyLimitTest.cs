using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NUnit.Framework;
using NzbDrone.Host;
using Seedarr.Api.V1.Packages;
using Seedarr.Api.V1.QBittorrent;
using Seedarr.Api.V1.Torrents;
using Seedarr.Http;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class RequestBodyLimitTest
{
    [Test]
    public void Kestrel_default_body_limit_is_64mb()
    {
        var serverOptions = new KestrelServerOptions();

        Bootstrap.ConfigureKestrelLimits(serverOptions);

        Assert.That(serverOptions.Limits.MaxRequestBodySize, Is.EqualTo(RequestBodyLimits.Default));
        Assert.That(serverOptions.Limits.MaxRequestBodySize, Is.LessThan(500L * 1024 * 1024));
    }

    [Test]
    public void Torrent_upload_rejects_bodies_above_the_parser_cap()
    {
        AssertLimit(typeof(TorrentController).GetMethod(nameof(TorrentController.Upload)), RequestBodyLimits.TorrentUpload);
    }

    [Test]
    public void QBittorrent_add_rejects_bodies_above_the_parser_cap()
    {
        AssertLimit(typeof(QBittorrentApiController).GetMethod(nameof(QBittorrentApiController.AddTorrents)), RequestBodyLimits.TorrentUpload);
    }

    [Test]
    public void Package_import_can_exceed_the_global_default()
    {
        AssertLimit(typeof(PackageController).GetMethod(nameof(PackageController.Import)), RequestBodyLimits.PackageImport);
        Assert.That(RequestBodyLimits.PackageImport, Is.GreaterThan(RequestBodyLimits.Default));
    }

    private static void AssertLimit(MethodInfo method, long expected)
    {
        Assert.That(method, Is.Not.Null);
        var requestLimit = method.GetCustomAttribute<RequestSizeLimitAttribute>();
        var formLimit = method.GetCustomAttribute<RequestFormLimitsAttribute>();
        Assert.That(requestLimit, Is.Not.Null);
        Assert.That(((IRequestSizeLimitMetadata)requestLimit).MaxRequestBodySize, Is.EqualTo(expected));
        Assert.That(formLimit, Is.Not.Null);
        Assert.That(formLimit.MultipartBodyLengthLimit, Is.EqualTo(expected));
    }
}
