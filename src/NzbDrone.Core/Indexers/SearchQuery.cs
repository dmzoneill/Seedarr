namespace NzbDrone.Core.Indexers;

public enum SearchMode
{
    Default = 0,
    Search = 0,
    TvSearch = 1,
    Movie = 2,
    Music = 3,
    Book = 4,
}

public class SearchQuery
{
    public SearchMode Mode { get; set; } = SearchMode.Default;

    public string Query { get; set; }

    public string Category { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; } = 50;

    // TV parameters
    public int? Season { get; set; }

    public int? Episode { get; set; }

    public string TvdbId { get; set; }

    public string Rid { get; set; }

    // Movie / Common ID parameters
    public string ImdbId { get; set; }

    public string TmdbId { get; set; }

    // Music parameters
    public string Artist { get; set; }

    public string Album { get; set; }

    // Book parameters
    public string Author { get; set; }

    public string Title { get; set; }

    public int? Year { get; set; }

    public string Categories { get; set; }
}
