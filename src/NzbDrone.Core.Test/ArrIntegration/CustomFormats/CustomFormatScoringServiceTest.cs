using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration.CustomFormats;

namespace NzbDrone.Core.Test.ArrIntegration.CustomFormats;

[TestFixture]
public class CustomFormatScoringServiceTest
{
    private CustomFormatScoringService _subject;

    [SetUp]
    public void Setup()
    {
        _subject = new CustomFormatScoringService();
    }

    [Test]
    public void Evaluate_should_score_positive_matches_for_dolby_vision_and_hdr10()
    {
        var releaseTitle = "The.Matrix.1999.2160p.UHD.BluRay.x265.DV.HDR10.TrueHD.Atmos.7.1-FraMeSToR";

        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 1,
                Name = "Dolby Vision",
                Specifications = new List<CustomFormatSpecification>
                {
                    new CustomFormatSpecification
                    {
                        Name = "DV",
                        Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, string> { { "value", @"\b(DV|Dolby\.?Vision)\b" } },
                    },
                },
            },
            new CustomFormat
            {
                Id = 2,
                Name = "HDR10",
                Specifications = new List<CustomFormatSpecification>
                {
                    new CustomFormatSpecification
                    {
                        Name = "HDR10",
                        Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, string> { { "value", @"\bHDR10\b" } },
                    },
                },
            },
        };

        var profileScores = new Dictionary<int, int>
        {
            { 1, 100 },
            { 2, 50 },
        };

        var result = _subject.Evaluate(releaseTitle, customFormats, profileScores);

        Assert.That(result.TotalScore, Is.EqualTo(150));
        Assert.That(result.MatchedFormats, Contains.Item("Dolby Vision"));
        Assert.That(result.MatchedFormats, Contains.Item("HDR10"));
    }

    [Test]
    public void Evaluate_should_apply_negative_score_for_unwanted_scene_groups()
    {
        var releaseTitle = "Some.Movie.2023.1080p.WEB-DL-EVO";

        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 3,
                Name = "Unwanted Scene Groups",
                Specifications = new List<CustomFormatSpecification>
                {
                    new CustomFormatSpecification
                    {
                        Name = "Bad Groups",
                        Implementation = "ReleaseGroupSpecification",
                        Fields = new Dictionary<string, string> { { "value", @"\b(EVO|TERMiNAL|YIFY)\b" } },
                    },
                },
            },
        };

        var profileScores = new Dictionary<int, int>
        {
            { 3, -1000 },
        };

        var result = _subject.Evaluate(releaseTitle, customFormats, profileScores);

        Assert.That(result.TotalScore, Is.EqualTo(-1000));
        Assert.That(result.MatchedFormats, Contains.Item("Unwanted Scene Groups"));
    }

    [Test]
    public void Evaluate_should_score_composite_with_positive_and_negative_matches()
    {
        var releaseTitle = "The.Matrix.1999.2160p.UHD.BluRay.x265.DV-EVO";

        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 1,
                Name = "Dolby Vision",
                Specifications = new List<CustomFormatSpecification>
                {
                    new CustomFormatSpecification
                    {
                        Name = "DV",
                        Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, string> { { "value", @"\b(DV|Dolby\.?Vision)\b" } },
                    },
                },
            },
            new CustomFormat
            {
                Id = 3,
                Name = "Unwanted Groups",
                Specifications = new List<CustomFormatSpecification>
                {
                    new CustomFormatSpecification
                    {
                        Name = "Bad Groups",
                        Implementation = "ReleaseGroupSpecification",
                        Fields = new Dictionary<string, string> { { "value", @"\b(EVO|TERMiNAL)\b" } },
                    },
                },
            },
        };

        var profileScores = new Dictionary<int, int>
        {
            { 1, 100 },
            { 3, -1000 },
        };

        var result = _subject.Evaluate(releaseTitle, customFormats, profileScores);

        Assert.That(result.TotalScore, Is.EqualTo(-900));
        Assert.That(result.MatchedFormats, Has.Count.EqualTo(2));
        Assert.That(result.MatchedFormats, Contains.Item("Dolby Vision"));
        Assert.That(result.MatchedFormats, Contains.Item("Unwanted Groups"));
    }

    [Test]
    public void Evaluate_should_handle_required_specification_logic()
    {
        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 10,
                Name = "4K Remux",
                Specifications = new List<CustomFormatSpecification>
                {
                    new CustomFormatSpecification
                    {
                        Name = "2160p",
                        Required = true,
                        Fields = new Dictionary<string, string> { { "value", @"2160p" } },
                    },
                    new CustomFormatSpecification
                    {
                        Name = "Remux",
                        Required = true,
                        Fields = new Dictionary<string, string> { { "value", @"Remux" } },
                    },
                },
            },
        };

        var profileScores = new Dictionary<int, int> { { 10, 200 } };

        // Missing 2160p -> required fails
        var res1 = _subject.Evaluate("Movie.1080p.Remux", customFormats, profileScores);
        Assert.That(res1.TotalScore, Is.EqualTo(0));
        Assert.That(res1.MatchedFormats, Is.Empty);

        // Missing Remux -> required fails
        var res2 = _subject.Evaluate("Movie.2160p.WEB-DL", customFormats, profileScores);
        Assert.That(res2.TotalScore, Is.EqualTo(0));
        Assert.That(res2.MatchedFormats, Is.Empty);

        // Has both -> matches
        var res3 = _subject.Evaluate("Movie.2160p.Remux", customFormats, profileScores);
        Assert.That(res3.TotalScore, Is.EqualTo(200));
        Assert.That(res3.MatchedFormats, Contains.Item("4K Remux"));
    }

    [Test]
    public void Evaluate_should_handle_negated_specification_logic()
    {
        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 20,
                Name = "1080p Clean",
                Specifications = new List<CustomFormatSpecification>
                {
                    new CustomFormatSpecification
                    {
                        Name = "1080p",
                        Required = true,
                        Negate = false,
                        Fields = new Dictionary<string, string> { { "value", @"1080p" } },
                    },
                    new CustomFormatSpecification
                    {
                        Name = "CAM",
                        Required = true,
                        Negate = true,
                        Fields = new Dictionary<string, string> { { "value", @"\b(CAM|TS|TELESYNC)\b" } },
                    },
                },
            },
        };

        var profileScores = new Dictionary<int, int> { { 20, 50 } };

        // Has 1080p but also CAM -> negated spec fails -> format does not match
        var resCam = _subject.Evaluate("Movie.2023.1080p.CAM", customFormats, profileScores);
        Assert.That(resCam.TotalScore, Is.EqualTo(0));
        Assert.That(resCam.MatchedFormats, Is.Empty);

        // Has 1080p and no CAM -> both pass -> matches
        var resClean = _subject.Evaluate("Movie.2023.1080p.BluRay", customFormats, profileScores);
        Assert.That(resClean.TotalScore, Is.EqualTo(50));
        Assert.That(resClean.MatchedFormats, Contains.Item("1080p Clean"));
    }

    [Test]
    public void Evaluate_should_handle_optional_specifications_any_match()
    {
        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 30,
                Name = "HDR",
                Specifications = new List<CustomFormatSpecification>
                {
                    new CustomFormatSpecification
                    {
                        Name = "DV",
                        Required = false,
                        Fields = new Dictionary<string, string> { { "value", @"\b(DV|Dolby\.?Vision)\b" } },
                    },
                    new CustomFormatSpecification
                    {
                        Name = "HDR10",
                        Required = false,
                        Fields = new Dictionary<string, string> { { "value", @"\bHDR10\b" } },
                    },
                },
            },
        };

        var profileScores = new Dictionary<int, int> { { 30, 75 } };

        // Matches one optional spec
        var resHdr = _subject.Evaluate("Movie.2023.1080p.HDR10.BluRay", customFormats, profileScores);
        Assert.That(resHdr.TotalScore, Is.EqualTo(75));
        Assert.That(resHdr.MatchedFormats, Contains.Item("HDR"));

        // Matches neither
        var resSdr = _subject.Evaluate("Movie.2023.1080p.SDR.BluRay", customFormats, profileScores);
        Assert.That(resSdr.TotalScore, Is.EqualTo(0));
        Assert.That(resSdr.MatchedFormats, Is.Empty);
    }

    [Test]
    public void Evaluate_should_return_empty_result_when_inputs_are_null_or_empty()
    {
        var resNullTitle = _subject.Evaluate(null, new List<CustomFormat>());
        Assert.That(resNullTitle.TotalScore, Is.EqualTo(0));
        Assert.That(resNullTitle.MatchedFormats, Is.Empty);

        var resNullFormats = _subject.Evaluate("Movie.2023.1080p", null);
        Assert.That(resNullFormats.TotalScore, Is.EqualTo(0));
        Assert.That(resNullFormats.MatchedFormats, Is.Empty);
    }
}
