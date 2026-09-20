using System;
using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Torznab;

namespace NzbDrone.Core.Test.Indexers.Torznab;

[TestFixture]
public class TorznabCapsParserTest
{
    private const string SampleCapsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
  <server version=""1.2"" title=""TestIndexer"" />
  <limits max=""100"" default=""50"" />
  <registration available=""yes"" open=""no"" />
  <searching>
    <search available=""yes"" supportedParams=""q"" />
    <tv-search available=""yes"" supportedParams=""q,season,ep,imdbid,tvdbid,rid"" />
    <movie-search available=""yes"" supportedParams=""q,imdbid,tmdbid"" />
    <music-search available=""no"" supportedParams=""q,artist,album"" />
    <book-search available=""yes"" supportedParams=""q,title,author"" />
  </searching>
  <categories>
    <category id=""1000"" name=""Console"">
      <subcat id=""1010"" name=""NDS"" />
      <subcat id=""1020"" name=""PSP"" />
    </category>
    <category id=""2000"" name=""Movies"" description=""Movie releases"">
      <subcat id=""2010"" name=""Foreign"" />
      <subcat id=""2040"" name=""HD"" />
      <subcat id=""2045"" name=""UHD"" />
    </category>
    <category id=""5000"" name=""TV"">
      <subcat id=""5030"" name=""SD"" />
      <subcat id=""5040"" name=""HD"" />
      <subcat id=""5070"" name=""Anime"" />
    </category>
    <category id=""100000"" name=""CustomTrackerCategory"">
      <subcat id=""100001"" name=""CustomSubcategory"" />
    </category>
  </categories>
</caps>";

    [Test]
    public void Parse_should_extract_server_info()
    {
        var caps = TorznabCapsParser.Parse(SampleCapsXml);

        Assert.That(caps, Is.Not.Null);
        Assert.That(caps.ServerTitle, Is.EqualTo("TestIndexer"));
        Assert.That(caps.ServerVersion, Is.EqualTo("1.2"));
    }

    [Test]
    public void Parse_should_extract_searching_capabilities()
    {
        var caps = TorznabCapsParser.Parse(SampleCapsXml);

        Assert.That(caps.SupportsSearch, Is.True);
        Assert.That(caps.SupportsTvSearch, Is.True);
        Assert.That(caps.SupportsMovieSearch, Is.True);
        Assert.That(caps.SupportsMusicSearch, Is.False);
        Assert.That(caps.SupportsBookSearch, Is.True);

        Assert.That(caps.Searching.ContainsKey("tv-search"), Is.True);
        var tvSearch = caps.Searching["tv-search"];
        Assert.That(tvSearch.Available, Is.True);
        Assert.That(tvSearch.SupportedParams, Does.Contain("season"));
        Assert.That(tvSearch.SupportedParams, Does.Contain("imdbid"));
        Assert.That(tvSearch.SupportedParams, Does.Contain("tvdbid"));

        var movieSearch = caps.Searching["movie-search"];
        Assert.That(movieSearch.SupportedParams, Does.Contain("tmdbid"));
    }

    [Test]
    public void Parse_should_extract_categories_and_subcategories()
    {
        var caps = TorznabCapsParser.Parse(SampleCapsXml);

        Assert.That(caps.Categories.Count, Is.EqualTo(4));

        var consoleCat = caps.Categories.First(c => c.Id == 1000);
        Assert.That(consoleCat.Name, Is.EqualTo("Console"));
        Assert.That(consoleCat.Subcategories.Count, Is.EqualTo(2));
        Assert.That(consoleCat.Subcategories.Any(s => s.Id == 1010 && s.Name == "NDS"), Is.True);
        Assert.That(consoleCat.Subcategories.Any(s => s.Id == 1020 && s.Name == "PSP"), Is.True);

        var movieCat = caps.Categories.First(c => c.Id == 2000);
        Assert.That(movieCat.Name, Is.EqualTo("Movies"));
        Assert.That(movieCat.Description, Is.EqualTo("Movie releases"));
        Assert.That(movieCat.Subcategories.Count, Is.EqualTo(3));
        Assert.That(movieCat.Subcategories.Any(s => s.Id == 2045 && s.Name == "UHD"), Is.True);

        var customCat = caps.Categories.First(c => c.Id == 100000);
        Assert.That(customCat.Name, Is.EqualTo("CustomTrackerCategory"));
        Assert.That(customCat.Subcategories.Count, Is.EqualTo(1));
        Assert.That(customCat.Subcategories[0].Id, Is.EqualTo(100001));
    }

    [Test]
    public void Parse_should_handle_xml_with_namespace()
    {
        const string xmlWithNs = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps xmlns=""http://torznab.com/schemas/2010/et/caps/"">
  <server version=""2.0"" title=""NamespacedIndexer"" />
  <searching>
    <movie-search available=""yes"" supportedParams=""q,imdbid"" />
  </searching>
  <categories>
    <category id=""2000"" name=""Movies"">
      <subcat id=""2040"" name=""HD"" />
    </category>
  </categories>
</caps>";

        var caps = TorznabCapsParser.Parse(xmlWithNs);

        Assert.That(caps.ServerTitle, Is.EqualTo("NamespacedIndexer"));
        Assert.That(caps.SupportsMovieSearch, Is.True);
        Assert.That(caps.Categories.Count, Is.EqualTo(1));
        Assert.That(caps.Categories[0].Subcategories[0].Id, Is.EqualTo(2040));
    }

    [Test]
    public void Parse_should_handle_empty_or_malformed_xml_gracefully()
    {
        var capsEmpty = TorznabCapsParser.Parse(string.Empty);
        Assert.That(capsEmpty, Is.Not.Null);
        Assert.That(capsEmpty.Categories, Is.Empty);

        var capsMalformed = TorznabCapsParser.Parse("<caps><unclosed>");
        Assert.That(capsMalformed, Is.Not.Null);
        Assert.That(capsMalformed.Categories, Is.Empty);
    }

    [Test]
    public void GetAllCategoryIds_should_return_all_root_and_subcategory_ids()
    {
        var caps = TorznabCapsParser.Parse(SampleCapsXml);
        var allIds = caps.GetAllCategoryIds();

        Assert.That(allIds, Does.Contain(1000));
        Assert.That(allIds, Does.Contain(1010));
        Assert.That(allIds, Does.Contain(1020));
        Assert.That(allIds, Does.Contain(2000));
        Assert.That(allIds, Does.Contain(2040));
        Assert.That(allIds, Does.Contain(100000));
        Assert.That(allIds, Does.Contain(100001));
    }
}
