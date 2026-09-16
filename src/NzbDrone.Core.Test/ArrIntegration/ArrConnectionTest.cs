using System;
using System.Net.Http;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class ArrConnectionTest
{
    [Test]
    public void NormalizeUrl_should_prepend_http_when_scheme_missing()
    {
        var result = ArrConnectionResources.NormalizeUrl("localhost:8989");

        Assert.That(result, Is.EqualTo("http://localhost:8989"));
    }

    [Test]
    public void NormalizeUrl_should_trim_trailing_slashes()
    {
        var result = ArrConnectionResources.NormalizeUrl("http://localhost:8989///");

        Assert.That(result, Is.EqualTo("http://localhost:8989"));
    }

    [Test]
    public void NormalizeUrl_should_preserve_https()
    {
        var result = ArrConnectionResources.NormalizeUrl("https://arr.local:8989/");

        Assert.That(result, Is.EqualTo("https://arr.local:8989"));
    }

    [Test]
    public void NormalizeUrl_should_handle_whitespace_and_null()
    {
        Assert.That(ArrConnectionResources.NormalizeUrl(null), Is.Null);
        Assert.That(ArrConnectionResources.NormalizeUrl("   "), Is.EqualTo("   "));
    }

    [TestCase("localhost:8989", true, "http://localhost:8989")]
    [TestCase("http://localhost:8989", true, "http://localhost:8989")]
    [TestCase("http://localhost:8989/", true, "http://localhost:8989")]
    [TestCase("https://arr.example.com:7878/api", true, "https://arr.example.com:7878/api")]
    [TestCase("https://arr.example.com:7878/api/", true, "https://arr.example.com:7878/api")]
    public void TryNormalizeUrl_should_succeed_for_valid_urls(string input, bool expectedSuccess, string expectedNormalized)
    {
        var success = ArrConnectionResources.TryNormalizeUrl(input, out var normalized, out var errorMessage);

        Assert.That(success, Is.EqualTo(expectedSuccess));
        Assert.That(normalized, Is.EqualTo(expectedNormalized));
        Assert.That(errorMessage, Is.Null);
    }

    [TestCase("", "URL cannot be empty")]
    [TestCase("   ", "URL cannot be empty")]
    [TestCase(null, "URL cannot be empty")]
    [TestCase("http://", "Invalid URL format: 'http://'")]
    [TestCase("ftp://localhost:8989", "Invalid URL format: 'ftp://localhost:8989'")]
    public void TryNormalizeUrl_should_fail_for_invalid_urls(string input, string expectedErrorSubstring)
    {
        var success = ArrConnectionResources.TryNormalizeUrl(input, out var normalized, out var errorMessage);

        Assert.That(success, Is.False);
        Assert.That(normalized, Is.Null);
        Assert.That(errorMessage, Does.Contain(expectedErrorSubstring));
    }

    [Test]
    public void GetClient_should_return_SharedClient_when_acceptInvalidCertificates_is_false()
    {
        var client = ArrConnectionResources.GetClient(false);

        Assert.That(client, Is.SameAs(ArrConnectionResources.SharedClient));
    }

    [Test]
    public void GetClient_should_return_SharedInsecureClient_when_acceptInvalidCertificates_is_true()
    {
        var client = ArrConnectionResources.GetClient(true);

        Assert.That(client, Is.SameAs(ArrConnectionResources.SharedInsecureClient));
        Assert.That(client, Is.Not.SameAs(ArrConnectionResources.SharedClient));
    }

    [Test]
    public void Connection_implementations_should_default_AcceptInvalidCertificates_to_false()
    {
        var sonarr = new SonarrConnection();
        var radarr = new RadarrConnection();
        var lidarr = new LidarrConnection();

        Assert.That(sonarr.AcceptInvalidCertificates, Is.False);
        Assert.That(radarr.AcceptInvalidCertificates, Is.False);
        Assert.That(lidarr.AcceptInvalidCertificates, Is.False);
    }

    [Test]
    public void Connection_implementations_should_allow_setting_AcceptInvalidCertificates()
    {
        var sonarr = new SonarrConnection { AcceptInvalidCertificates = true };
        var radarr = new RadarrConnection { AcceptInvalidCertificates = true };
        var lidarr = new LidarrConnection { AcceptInvalidCertificates = true };

        Assert.That(sonarr.AcceptInvalidCertificates, Is.True);
        Assert.That(radarr.AcceptInvalidCertificates, Is.True);
        Assert.That(lidarr.AcceptInvalidCertificates, Is.True);
    }

    [Test]
    public void Connection_implementations_should_normalize_url_on_set()
    {
        var sonarr = new SonarrConnection { Url = "localhost:8989/" };
        var radarr = new RadarrConnection { Url = "localhost:7878/" };
        var lidarr = new LidarrConnection { Url = "localhost:8686/" };

        Assert.That(sonarr.Url, Is.EqualTo("http://localhost:8989"));
        Assert.That(radarr.Url, Is.EqualTo("http://localhost:7878"));
        Assert.That(lidarr.Url, Is.EqualTo("http://localhost:8686"));
    }

    [Test]
    public void TestConnectionDetailed_should_fail_when_url_is_invalid()
    {
        var sonarr = new SonarrConnection { Url = "" };
        var radarr = new RadarrConnection { Url = "" };
        var lidarr = new LidarrConnection { Url = "" };

        var sonarrResult = sonarr.TestConnectionDetailed();
        var radarrResult = radarr.TestConnectionDetailed();
        var lidarrResult = lidarr.TestConnectionDetailed();

        Assert.That(sonarrResult.Success, Is.False);
        Assert.That(sonarrResult.Message, Does.Contain("URL cannot be empty"));

        Assert.That(radarrResult.Success, Is.False);
        Assert.That(radarrResult.Message, Does.Contain("URL cannot be empty"));

        Assert.That(lidarrResult.Success, Is.False);
        Assert.That(lidarrResult.Message, Does.Contain("URL cannot be empty"));
    }

    [Test]
    public void ArrConnectionDefinition_should_clamp_SyncIntervalMinutes_to_at_least_one()
    {
        var def = new ArrConnectionDefinition();
        Assert.That(def.SyncIntervalMinutes, Is.EqualTo(60));

        def.SyncIntervalMinutes = 0;
        Assert.That(def.SyncIntervalMinutes, Is.EqualTo(1));

        def.SyncIntervalMinutes = -15;
        Assert.That(def.SyncIntervalMinutes, Is.EqualTo(1));

        def.SyncIntervalMinutes = 30;
        Assert.That(def.SyncIntervalMinutes, Is.EqualTo(30));
    }

    [Test]
    public void ArrConnectionDefinition_should_default_AcceptInvalidCertificates_to_false()
    {
        var def = new ArrConnectionDefinition();

        Assert.That(def.AcceptInvalidCertificates, Is.False);
    }

    [Test]
    public void ArrConnectionDefinition_Clone_should_preserve_properties()
    {
        var original = new ArrConnectionDefinition
        {
            Name = "My Sonarr",
            ArrType = "Sonarr",
            Url = "https://sonarr.local:8989",
            ApiKey = "abc123secret",
            AcceptInvalidCertificates = true,
            SyncIntervalMinutes = 15
        };

        var clone = original.Clone();

        Assert.That(clone.Name, Is.EqualTo(original.Name));
        Assert.That(clone.ArrType, Is.EqualTo(original.ArrType));
        Assert.That(clone.Url, Is.EqualTo(original.Url));
        Assert.That(clone.ApiKey, Is.EqualTo(original.ApiKey));
        Assert.That(clone.AcceptInvalidCertificates, Is.True);
        Assert.That(clone.SyncIntervalMinutes, Is.EqualTo(15));
    }
}
