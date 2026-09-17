using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaEnrichment;

public interface ITmdbMetadataProvider
{
    string ApiKey { get; set; }

    Task<TorrentMediaMetadata> SearchMovieAsync(string cleanTitle, int? year = null, CancellationToken cancellationToken = default);

    Task<TorrentMediaMetadata> SearchTvAsync(string cleanTitle, int? year = null, CancellationToken cancellationToken = default);

    Task<TorrentMediaMetadata> FindByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default);

    Task<TorrentMediaMetadata> GetMovieDetailsAsync(int tmdbId, CancellationToken cancellationToken = default);

    Task<TorrentMediaMetadata> GetTvDetailsAsync(int tmdbId, CancellationToken cancellationToken = default);

    Task<TorrentMediaMetadata> LookupMediaAsync(string title, int? year = null, string imdbId = null, string mediaType = null, CancellationToken cancellationToken = default);
}
