using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Blocklist;

public interface IBlocklistArchiveStreamProvider
{
    IAsyncEnumerable<string> ReadLinesAsync(
        Stream stream,
        string url = null,
        string contentType = null,
        string contentEncoding = null,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ReadLinesAsync(
        HttpResponseMessage response,
        string url = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ReadRulesAsync(
        Stream stream,
        string url = null,
        string contentType = null,
        string contentEncoding = null,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ReadRulesAsync(
        HttpResponseMessage response,
        string url = null,
        CancellationToken cancellationToken = default);

    Task<List<string>> ExtractRulesAsync(
        Stream stream,
        string url = null,
        string contentType = null,
        string contentEncoding = null,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default);

    Task<List<string>> ExtractRulesAsync(
        HttpResponseMessage response,
        string url = null,
        CancellationToken cancellationToken = default);
}
