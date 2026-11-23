using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
    public void Torrent_upload_caps_each_file_not_the_whole_request()
    {
        AssertLimit(
            typeof(TorrentController).GetMethod(nameof(TorrentController.Upload)),
            RequestBodyLimits.TorrentUploadRequest,
            RequestBodyLimits.TorrentUploadPerFile);
    }

    [Test]
    public void QBittorrent_add_caps_each_file_not_the_whole_request()
    {
        AssertLimit(
            typeof(QBittorrentApiController).GetMethod(nameof(QBittorrentApiController.AddTorrents)),
            RequestBodyLimits.TorrentUploadRequest,
            RequestBodyLimits.TorrentUploadPerFile);
    }

    [Test]
    public void Torrent_upload_request_fits_several_max_size_files()
    {
        Assert.That(RequestBodyLimits.TorrentUploadPerFile, Is.EqualTo(10L * 1024 * 1024));
        Assert.That(RequestBodyLimits.TorrentUploadRequest, Is.GreaterThan(RequestBodyLimits.TorrentUploadPerFile * 2));
    }

    [Test]
    public void Package_import_can_exceed_the_global_default()
    {
        AssertLimit(
            typeof(PackageController).GetMethod(nameof(PackageController.Import)),
            RequestBodyLimits.PackageImport,
            RequestBodyLimits.PackageImport);
        Assert.That(RequestBodyLimits.PackageImport, Is.GreaterThan(RequestBodyLimits.Default));
    }

    [Test]
    public async Task Multi_file_upload_whose_total_exceeds_the_per_file_cap_is_accepted()
    {
        var sixMiB = 6 * 1024 * 1024;
        var request = CreateMultipartRequest(sixMiB, sixMiB);

        var form = await request.ReadFormAsync();

        Assert.That(form.Files, Has.Count.EqualTo(2));
        Assert.That(form.Files.Sum(file => file.Length), Is.GreaterThan(RequestBodyLimits.TorrentUploadPerFile));
    }

    [Test]
    public void Single_file_above_the_per_file_cap_is_rejected()
    {
        var request = CreateMultipartRequest((int)RequestBodyLimits.TorrentUploadPerFile + 1024);

        Assert.ThrowsAsync<InvalidDataException>(async () => await request.ReadFormAsync());
    }

    private static HttpRequest CreateMultipartRequest(params int[] fileSizes)
    {
        const string boundary = "seedarr-test-boundary";
        var body = new MemoryStream();
        foreach (var (size, index) in fileSizes.Select((size, index) => (size, index)))
        {
            var header = $"--{boundary}\r\n" +
                         $"Content-Disposition: form-data; name=\"file\"; filename=\"t{index}.torrent\"\r\n" +
                         "Content-Type: application/x-bittorrent\r\n\r\n";
            body.Write(Encoding.ASCII.GetBytes(header));
            body.Write(new byte[size]);
            body.Write(Encoding.ASCII.GetBytes("\r\n"));
        }

        body.Write(Encoding.ASCII.GetBytes($"--{boundary}--\r\n"));
        body.Position = 0;

        var context = new DefaultHttpContext();
        context.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        context.Request.Body = body;
        context.Request.ContentLength = body.Length;
        context.Features.Set<IFormFeature>(new FormFeature(context.Request, new FormOptions
        {
            MultipartBodyLengthLimit = RequestBodyLimits.TorrentUploadPerFile
        }));

        return context.Request;
    }

    private static void AssertLimit(MethodInfo method, long expectedRequest, long expectedPerFile)
    {
        Assert.That(method, Is.Not.Null);
        var requestLimit = method.GetCustomAttribute<RequestSizeLimitAttribute>();
        var formLimit = method.GetCustomAttribute<RequestFormLimitsAttribute>();
        Assert.That(requestLimit, Is.Not.Null);
        Assert.That(((IRequestSizeLimitMetadata)requestLimit).MaxRequestBodySize, Is.EqualTo(expectedRequest));
        Assert.That(formLimit, Is.Not.Null);
        Assert.That(formLimit.MultipartBodyLengthLimit, Is.EqualTo(expectedPerFile));
    }
}
