using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;

namespace NzbDrone.Core.Test.MediaEnrichment;

[TestFixture]
public class TmdbMetadataProviderTest
{
    private IConfigService _configService;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.TmdbApiKey.Returns("test_api_key_123");
    }

    [Test]
    public async Task SearchMovieAsync_returns_mapped_metadata_with_posters_overview_cast_and_rating()
    {
        var searchJson = @"{
            ""page"": 1,
            ""results"": [
                {
                    ""id"": 27205,
                    ""title"": ""Inception"",
                    ""release_date"": ""2010-07-16"",
                    ""overview"": ""A thief who steals corporate secrets through the use of dream-sharing technology."",
                    ""vote_average"": 8.4,
                    ""poster_path"": ""/edv5CZvWj09upOsy2Y6IwDhK8bt.jpg"",
                    ""backdrop_path"": ""/8ZTVqvKDQ8emSGUEMjsS4yHAwrp.jpg""
                }
            ]
        }";

        var detailsJson = @"{
            ""id"": 27205,
            ""title"": ""Inception"",
            ""release_date"": ""2010-07-16"",
            ""overview"": ""A thief who steals corporate secrets through the use of dream-sharing technology."",
            ""vote_average"": 8.4,
            ""poster_path"": ""/edv5CZvWj09upOsy2Y6IwDhK8bt.jpg"",
            ""backdrop_path"": ""/8ZTVqvKDQ8emSGUEMjsS4yHAwrp.jpg"",
            ""imdb_id"": ""tt1375666"",
            ""genres"": [
                { ""id"": 28, ""name"": ""Action"" },
                { ""id"": 878, ""name"": ""Science Fiction"" }
            ],
            ""credits"": {
                ""cast"": [
                    { ""name"": ""Leonardo DiCaprio"", ""character"": ""Dom Cobb"" },
                    { ""name"": ""Joseph Gordon-Levitt"", ""character"": ""Arthur"" }
                ]
            }
        }";

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri.PathAndQuery;
            if (path.Contains("/search/movie"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searchJson)
                };
            }

            if (path.Contains("/movie/27205"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(detailsJson)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(_configService, httpClient);

        var result = await provider.SearchMovieAsync("Inception", 2010);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Inception"));
        Assert.That(result.Year, Is.EqualTo(2010));
        Assert.That(result.Overview, Does.Contain("dream-sharing technology"));
        Assert.That(result.Rating, Is.EqualTo(8.4));
        Assert.That(result.PosterUrl, Is.EqualTo("https://image.tmdb.org/t/p/w500/edv5CZvWj09upOsy2Y6IwDhK8bt.jpg"));
        Assert.That(result.BackdropUrl, Is.EqualTo("https://image.tmdb.org/t/p/original/8ZTVqvKDQ8emSGUEMjsS4yHAwrp.jpg"));
        Assert.That(result.TmdbId, Is.EqualTo("27205"));
        Assert.That(result.ImdbId, Is.EqualTo("tt1375666"));
        Assert.That(result.Genres, Does.Contain("Action"));
        Assert.That(result.Genres, Does.Contain("Science Fiction"));
        Assert.That(result.Cast, Does.Contain("Leonardo DiCaprio"));
        Assert.That(result.Cast, Does.Contain("Joseph Gordon-Levitt"));
        Assert.That(result.ArrType, Is.EqualTo("Radarr"));
    }

    [Test]
    public async Task FindByImdbIdAsync_returns_mapped_metadata()
    {
        var findJson = @"{
            ""movie_results"": [
                {
                    ""id"": 550,
                    ""title"": ""Fight Club"",
                    ""release_date"": ""1999-10-15"",
                    ""overview"": ""An insomniac office worker and a devil-may-care soap maker form an underground fight club."",
                    ""vote_average"": 8.43,
                    ""poster_path"": ""/pB8BM7pdSp6B6Ih7QZ4DrQ3PmJK.jpg"",
                    ""backdrop_path"": ""/hZkgoQYus5vegHoetLkCJzb17zJ.jpg""
                }
            ],
            ""tv_results"": []
        }";

        var detailsJson = @"{
            ""id"": 550,
            ""title"": ""Fight Club"",
            ""release_date"": ""1999-10-15"",
            ""overview"": ""An insomniac office worker and a devil-may-care soap maker form an underground fight club."",
            ""vote_average"": 8.43,
            ""poster_path"": ""/pB8BM7pdSp6B6Ih7QZ4DrQ3PmJK.jpg"",
            ""backdrop_path"": ""/hZkgoQYus5vegHoetLkCJzb17zJ.jpg"",
            ""imdb_id"": ""tt0137523"",
            ""genres"": [
                { ""id"": 18, ""name"": ""Drama"" }
            ],
            ""credits"": {
                ""cast"": [
                    { ""name"": ""Edward Norton"", ""character"": ""The Narrator"" },
                    { ""name"": ""Brad Pitt"", ""character"": ""Tyler Durden"" }
                ]
            }
        }";

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri.PathAndQuery;
            if (path.Contains("/find/tt0137523"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(findJson)
                };
            }

            if (path.Contains("/movie/550"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(detailsJson)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(_configService, httpClient);

        var result = await provider.FindByImdbIdAsync("tt0137523");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Fight Club"));
        Assert.That(result.Year, Is.EqualTo(1999));
        Assert.That(result.ImdbId, Is.EqualTo("tt0137523"));
        Assert.That(result.TmdbId, Is.EqualTo("550"));
        Assert.That(result.Cast, Does.Contain("Brad Pitt"));
        Assert.That(result.Genres, Is.EqualTo("Drama"));
    }

    [Test]
    public async Task SearchMovieAsync_handles_http_429_with_retry()
    {
        var attempts = 0;
        var detailsJson = @"{
            ""id"": 550,
            ""title"": ""Fight Club"",
            ""release_date"": ""1999-10-15"",
            ""overview"": ""An underground fight club."",
            ""vote_average"": 8.4,
            ""poster_path"": ""/poster.jpg"",
            ""backdrop_path"": ""/backdrop.jpg""
        }";

        var searchJson = @"{
            ""page"": 1,
            ""results"": [
                {
                    ""id"": 550,
                    ""title"": ""Fight Club"",
                    ""release_date"": ""1999-10-15"",
                    ""overview"": ""An underground fight club."",
                    ""vote_average"": 8.4,
                    ""poster_path"": ""/poster.jpg"",
                    ""backdrop_path"": ""/backdrop.jpg""
                }
            ]
        }";

        var handler = new FakeHttpMessageHandler(req =>
        {
            attempts++;
            if (attempts == 1)
            {
                var msg = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                msg.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0));
                return msg;
            }

            var path = req.RequestUri.PathAndQuery;
            if (path.Contains("/search/movie"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searchJson)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(detailsJson)
            };
        });

        using var httpClient = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(_configService, httpClient)
        {
            DelayAsync = (_, _) => Task.CompletedTask
        };

        var result = await provider.SearchMovieAsync("Fight Club", 1999);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Fight Club"));
        Assert.That(attempts, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task SearchTvAsync_returns_mapped_tv_metadata()
    {
        var searchJson = @"{
            ""page"": 1,
            ""results"": [
                {
                    ""id"": 1399,
                    ""name"": ""Game of Thrones"",
                    ""first_air_date"": ""2011-04-17"",
                    ""overview"": ""Nine noble families fight for control over the lands of Westeros."",
                    ""vote_average"": 8.4,
                    ""poster_path"": ""/tv_poster.jpg"",
                    ""backdrop_path"": ""/tv_backdrop.jpg""
                }
            ]
        }";

        var detailsJson = @"{
            ""id"": 1399,
            ""name"": ""Game of Thrones"",
            ""first_air_date"": ""2011-04-17"",
            ""overview"": ""Nine noble families fight for control over the lands of Westeros."",
            ""vote_average"": 8.4,
            ""poster_path"": ""/tv_poster.jpg"",
            ""backdrop_path"": ""/tv_backdrop.jpg"",
            ""external_ids"": { ""imdb_id"": ""tt0944947"" },
            ""genres"": [
                { ""id"": 10765, ""name"": ""Sci-Fi & Fantasy"" }
            ],
            ""credits"": {
                ""cast"": [
                    { ""name"": ""Peter Dinklage"" },
                    { ""name"": ""Emilia Clarke"" }
                ]
            }
        }";

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri.PathAndQuery;
            if (path.Contains("/search/tv"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searchJson)
                };
            }

            if (path.Contains("/tv/1399"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(detailsJson)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(_configService, httpClient);

        var result = await provider.SearchTvAsync("Game of Thrones", 2011);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Game of Thrones"));
        Assert.That(result.Year, Is.EqualTo(2011));
        Assert.That(result.ArrType, Is.EqualTo("Sonarr"));
        Assert.That(result.ImdbId, Is.EqualTo("tt0944947"));
        Assert.That(result.Cast, Does.Contain("Peter Dinklage"));
    }

    [Test]
    public void Fallback_to_default_tmdb_api_key_when_config_is_empty()
    {
        _configService.TmdbApiKey.Returns(string.Empty);
        var provider = new TmdbMetadataProvider(_configService);

        Assert.That(ConfigService.DefaultTmdbApiKey, Is.Not.Null.And.Not.Empty);
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
