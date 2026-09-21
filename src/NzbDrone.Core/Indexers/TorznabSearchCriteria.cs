namespace NzbDrone.Core.Indexers;

public class TorznabSearchCriteria
{
    public string SearchType { get; set; }

    public string Query { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public string ImdbId { get; set; }

    public string TmdbId { get; set; }

    public string TvdbId { get; set; }

    public string Rid { get; set; }

    public string Artist { get; set; }

    public string Album { get; set; }

    public string Author { get; set; }

    public string Title { get; set; }

    public int? Year { get; set; }

    public string Categories { get; set; }

    public string Category { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; } = 50;

    public SearchMode DetermineSearchMode()
    {
        if (!string.IsNullOrWhiteSpace(SearchType))
        {
            var st = SearchType.Trim().ToLowerInvariant();
            if (st == "tvsearch" || st == "tv")
            {
                return SearchMode.TvSearch;
            }

            if (st == "movie")
            {
                return SearchMode.Movie;
            }

            if (st == "music")
            {
                return SearchMode.Music;
            }

            if (st == "book")
            {
                return SearchMode.Book;
            }

            if (st == "rss")
            {
                return SearchMode.Rss;
            }

            if (st == "search")
            {
                return SearchMode.Search;
            }
        }

        // Infer mode
        if (Season.HasValue || Episode.HasValue || !string.IsNullOrWhiteSpace(TvdbId) || !string.IsNullOrWhiteSpace(Rid))
        {
            return SearchMode.TvSearch;
        }

        if (!string.IsNullOrWhiteSpace(ImdbId) || !string.IsNullOrWhiteSpace(TmdbId))
        {
            return SearchMode.Movie;
        }

        if (!string.IsNullOrWhiteSpace(Artist) || !string.IsNullOrWhiteSpace(Album))
        {
            return SearchMode.Music;
        }

        if (!string.IsNullOrWhiteSpace(Author))
        {
            return SearchMode.Book;
        }

        return SearchMode.Search;
    }

    public SearchQuery ToSearchQuery()
    {
        var mode = DetermineSearchMode();
        var cat = !string.IsNullOrWhiteSpace(Categories) ? Categories : Category;

        return new SearchQuery
        {
            Mode = mode,
            Query = Query,
            Category = cat,
            Categories = cat,
            Offset = Offset,
            Limit = Limit > 0 ? Limit : 50,
            Season = Season,
            Episode = Episode,
            TvdbId = TvdbId,
            Rid = Rid,
            ImdbId = ImdbId,
            TmdbId = TmdbId,
            Artist = Artist,
            Album = Album,
            Author = Author,
            Title = Title,
            Year = Year
        };
    }
}
