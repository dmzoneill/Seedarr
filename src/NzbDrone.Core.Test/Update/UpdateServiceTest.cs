using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.Test.TestHelpers;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.Update;

[TestFixture]
public class UpdateServiceTest
{
    private UpdateService _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new UpdateService();
    }

    // --- BuildResult tests (private static, via reflection) ---

    private static UpdateInfo InvokeBuildResult(Version currentVersion, Version latestVersion, List<ReleaseInfo> releases)
    {
        var method = typeof(UpdateService).GetMethod("BuildResult", BindingFlags.NonPublic | BindingFlags.Static);
        return (UpdateInfo)method.Invoke(null, new object[] { currentVersion, latestVersion, releases });
    }

    [Test]
    public void BuildResult_should_set_current_version()
    {
        var current = new Version(1, 0, 0);

        var result = InvokeBuildResult(current, null, new List<ReleaseInfo>());

        Assert.That(result.CurrentVersion, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void BuildResult_should_set_latest_version_when_provided()
    {
        var current = new Version(1, 0, 0);
        var latest = new Version(2, 0, 0);

        var result = InvokeBuildResult(current, latest, new List<ReleaseInfo>());

        Assert.That(result.LatestVersion, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void BuildResult_should_set_latest_version_null_when_not_provided()
    {
        var current = new Version(1, 0, 0);

        var result = InvokeBuildResult(current, null, new List<ReleaseInfo>());

        Assert.That(result.LatestVersion, Is.Null);
    }

    [Test]
    public void BuildResult_should_set_update_available_true_when_newer_version_exists()
    {
        var current = new Version(1, 0, 0);
        var latest = new Version(2, 0, 0);

        var result = InvokeBuildResult(current, latest, new List<ReleaseInfo>());

        Assert.That(result.UpdateAvailable, Is.True);
    }

    [Test]
    public void BuildResult_should_set_update_available_false_when_on_latest()
    {
        var current = new Version(1, 0, 0);
        var latest = new Version(1, 0, 0);

        var result = InvokeBuildResult(current, latest, new List<ReleaseInfo>());

        Assert.That(result.UpdateAvailable, Is.False);
    }

    [Test]
    public void BuildResult_should_set_update_available_false_when_ahead_of_latest()
    {
        var current = new Version(3, 0, 0);
        var latest = new Version(2, 0, 0);

        var result = InvokeBuildResult(current, latest, new List<ReleaseInfo>());

        Assert.That(result.UpdateAvailable, Is.False);
    }

    [Test]
    public void BuildResult_should_set_update_available_false_when_latest_is_null()
    {
        var current = new Version(1, 0, 0);

        var result = InvokeBuildResult(current, null, new List<ReleaseInfo>());

        Assert.That(result.UpdateAvailable, Is.False);
    }

    [Test]
    public void BuildResult_should_include_releases()
    {
        var current = new Version(1, 0, 0);
        var releases = new List<ReleaseInfo>
        {
            new() { Version = "1.0.0", Body = "First release" },
            new() { Version = "1.1.0", Body = "Second release" },
        };

        var result = InvokeBuildResult(current, null, releases);

        Assert.That(result.Releases, Has.Count.EqualTo(2));
    }

    [Test]
    public void BuildResult_should_handle_empty_releases()
    {
        var current = new Version(1, 0, 0);

        var result = InvokeBuildResult(current, null, new List<ReleaseInfo>());

        Assert.That(result.Releases, Is.Empty);
    }

    // --- Caching tests (via reflection to manipulate private fields) ---

    private void SetCachedResult(UpdateInfo info, DateTime expiry)
    {
        var cachedField = typeof(UpdateService).GetField("_cachedResult", BindingFlags.NonPublic | BindingFlags.Instance);
        var expiryField = typeof(UpdateService).GetField("_cacheExpiry", BindingFlags.NonPublic | BindingFlags.Instance);
        cachedField.SetValue(_subject, info);
        expiryField.SetValue(_subject, expiry);
    }

    [Test]
    public void CheckForUpdate_should_return_cached_result_when_not_expired()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "2.0.0",
            UpdateAvailable = true,
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.CheckForUpdate();

        Assert.That(result, Is.SameAs(cached));
    }

    [Test]
    public void CheckForUpdate_should_return_cached_values_unchanged()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "3.5.0",
            UpdateAvailable = true,
            Releases = new List<ReleaseInfo>
            {
                new() { Version = "3.5.0", Body = "notes" },
            },
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(1));

        var result = _subject.CheckForUpdate();

        Assert.That(result.CurrentVersion, Is.EqualTo("1.0.0"));
        Assert.That(result.LatestVersion, Is.EqualTo("3.5.0"));
        Assert.That(result.UpdateAvailable, Is.True);
        Assert.That(result.Releases, Has.Count.EqualTo(1));
    }

    [Test]
    public void CheckForUpdate_consecutive_calls_return_same_cached_instance()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result1 = _subject.CheckForUpdate();
        var result2 = _subject.CheckForUpdate();

        Assert.That(result1, Is.SameAs(result2));
    }

    [Test]
    public void CheckForUpdate_should_fetch_when_cache_expired()
    {
        var oldCached = new UpdateInfo
        {
            CurrentVersion = "OLD",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(oldCached, DateTime.UtcNow.AddHours(-1));

        // This will attempt a real HTTP call which may fail, but it will return a new result
        var result = _subject.CheckForUpdate();

        // The result should not be the old cached object (it either fetched a new one or built a fallback)
        Assert.That(result, Is.Not.SameAs(oldCached));
    }

    [Test]
    public void CheckForUpdate_after_expired_cache_should_return_non_null_result()
    {
        SetCachedResult(null, DateTime.MinValue);

        // Will attempt HTTP call - may fail gracefully and return fallback result
        var result = _subject.CheckForUpdate();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.CurrentVersion, Is.Not.Null);
        Assert.That(result.Releases, Is.Not.Null);
    }

    // --- GetLatestVersion tests ---

    [Test]
    public void GetLatestVersion_should_return_parsed_version_from_cache()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "2.3.4",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.GetLatestVersion();

        Assert.That(result, Is.EqualTo(new Version(2, 3, 4)));
    }

    [Test]
    public void GetLatestVersion_should_return_null_when_latest_version_is_null()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = null,
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.GetLatestVersion();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetLatestVersion_should_return_null_when_latest_version_is_unparseable()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "not-a-version",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.GetLatestVersion();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetLatestVersion_should_return_null_when_latest_version_is_empty()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.GetLatestVersion();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetLatestVersion_should_parse_three_part_version()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "10.20.30",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.GetLatestVersion();

        Assert.That(result.Major, Is.EqualTo(10));
        Assert.That(result.Minor, Is.EqualTo(20));
        Assert.That(result.Build, Is.EqualTo(30));
    }

    [Test]
    public void GetLatestVersion_should_parse_four_part_version()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "1.2.3.4",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.GetLatestVersion();

        Assert.That(result, Is.EqualTo(new Version(1, 2, 3, 4)));
    }

    // --- Data model tests ---

    [Test]
    public void UpdateInfo_should_have_default_empty_releases_list()
    {
        var info = new UpdateInfo();

        Assert.That(info.Releases, Is.Not.Null);
        Assert.That(info.Releases, Is.Empty);
    }

    [Test]
    public void UpdateInfo_properties_should_be_settable()
    {
        var info = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "2.0.0",
            UpdateAvailable = true,
            ReleaseUrl = "https://example.com",
            ReleaseNotes = "Fixed bugs",
        };

        Assert.That(info.CurrentVersion, Is.EqualTo("1.0.0"));
        Assert.That(info.LatestVersion, Is.EqualTo("2.0.0"));
        Assert.That(info.UpdateAvailable, Is.True);
        Assert.That(info.ReleaseUrl, Is.EqualTo("https://example.com"));
        Assert.That(info.ReleaseNotes, Is.EqualTo("Fixed bugs"));
    }

    [Test]
    public void ReleaseInfo_properties_should_be_settable()
    {
        var now = DateTime.UtcNow;
        var info = new ReleaseInfo
        {
            Version = "1.2.3",
            PublishedAt = now,
            Body = "Release notes here",
            Url = "https://github.com/release/1",
        };

        Assert.That(info.Version, Is.EqualTo("1.2.3"));
        Assert.That(info.PublishedAt, Is.EqualTo(now));
        Assert.That(info.Body, Is.EqualTo("Release notes here"));
        Assert.That(info.Url, Is.EqualTo("https://github.com/release/1"));
    }

    // --- FetchUpdateInfo fallback behavior tests ---

    [Test]
    public void CheckForUpdate_with_no_cache_returns_result_with_current_version()
    {
        // No cache set, will try HTTP and likely fail (no network in test), but handles gracefully
        var result = _subject.CheckForUpdate();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.CurrentVersion, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void CheckForUpdate_with_no_cache_returns_non_null_releases_list()
    {
        var result = _subject.CheckForUpdate();

        Assert.That(result.Releases, Is.Not.Null);
    }

    [Test]
    public void CheckForUpdate_populates_cache_after_fetch()
    {
        SetCachedResult(null, DateTime.MinValue);

        _subject.CheckForUpdate();

        // Call again immediately - should now return cached
        var cachedField = typeof(UpdateService).GetField("_cachedResult", BindingFlags.NonPublic | BindingFlags.Instance);
        var cachedValue = cachedField.GetValue(_subject);

        Assert.That(cachedValue, Is.Not.Null);
    }

    [Test]
    public void CheckForUpdate_sets_cache_expiry_after_fetch()
    {
        SetCachedResult(null, DateTime.MinValue);

        _subject.CheckForUpdate();

        var expiryField = typeof(UpdateService).GetField("_cacheExpiry", BindingFlags.NonPublic | BindingFlags.Instance);
        var expiryValue = (DateTime)expiryField.GetValue(_subject);

        Assert.That(expiryValue, Is.GreaterThan(DateTime.UtcNow));
    }

    [Test]
    public void CheckForUpdate_should_fetch_when_cache_is_null_despite_future_expiry()
    {
        // Null cache with a future expiry — the null check short-circuits before expiry check.
        // The method must still go to FetchUpdateInfo() and return a fresh result.
        SetCachedResult(null, DateTime.UtcNow.AddHours(5));

        var result = _subject.CheckForUpdate();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.CurrentVersion, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Releases, Is.Not.Null);
    }

    [Test]
    public void CheckForUpdate_updates_cache_after_null_cache_fetch()
    {
        SetCachedResult(null, DateTime.UtcNow.AddHours(5));

        _subject.CheckForUpdate();

        var cachedField = typeof(UpdateService).GetField("_cachedResult", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(cachedField.GetValue(_subject), Is.Not.Null);
    }

    [Test]
    public void GetLatestVersion_should_return_version_with_major_minor_build()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "5.0.0",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.GetLatestVersion();

        Assert.That(result.Major, Is.EqualTo(5));
        Assert.That(result.Minor, Is.EqualTo(0));
        Assert.That(result.Build, Is.EqualTo(0));
    }

    [Test]
    public void BuildResult_should_set_update_available_false_when_versions_are_equal()
    {
        var current = new Version(2, 5, 1);
        var latest = new Version(2, 5, 1);

        var result = InvokeBuildResult(current, latest, new List<ReleaseInfo>());

        Assert.That(result.UpdateAvailable, Is.False);
    }

    // --- FetchUpdateInfo tests via HTTP injection ---

    private static UpdateService CreateWithHandler(MockHttpMessageHandler handler)
    {
        return new UpdateService(new HttpClient(handler));
    }

    private static UpdateService CreateWithThrowingHandler(Exception ex)
    {
        return new UpdateService(new HttpClient(new ThrowingHttpMessageHandler(ex)));
    }

    [Test]
    public void CheckForUpdate_should_return_fallback_when_api_returns_non_success_status()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "");
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.UpdateAvailable, Is.False);
        Assert.That(result.Releases, Is.Empty);
        Assert.That(result.LatestVersion, Is.Null);
    }

    [Test]
    public void CheckForUpdate_should_return_fallback_when_response_is_not_json_array()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"message\": \"API rate limit exceeded\"}");
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.UpdateAvailable, Is.False);
        Assert.That(result.Releases, Is.Empty);
        Assert.That(result.LatestVersion, Is.Null);
    }

    [Test]
    public void CheckForUpdate_should_return_fallback_when_json_is_invalid()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "not valid json {{{{");
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.UpdateAvailable, Is.False);
        Assert.That(result.Releases, Is.Empty);
    }

    [Test]
    public void CheckForUpdate_should_return_fallback_on_http_request_exception()
    {
        var subject = CreateWithThrowingHandler(new HttpRequestException("no network"));

        var result = subject.CheckForUpdate();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.UpdateAvailable, Is.False);
        Assert.That(result.Releases, Is.Empty);
    }

    [Test]
    public void CheckForUpdate_should_return_fallback_on_unexpected_exception()
    {
        var subject = CreateWithThrowingHandler(new InvalidOperationException("unexpected"));

        var result = subject.CheckForUpdate();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.UpdateAvailable, Is.False);
        Assert.That(result.Releases, Is.Empty);
    }

    [Test]
    public void CheckForUpdate_should_return_empty_releases_for_empty_array_response()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Is.Empty);
        Assert.That(result.LatestVersion, Is.Null);
        Assert.That(result.UpdateAvailable, Is.False);
    }

    [Test]
    public void CheckForUpdate_should_parse_single_release_from_response()
    {
        var json = """
            [
              {
                "tag_name": "v1.2.3",
                "draft": false,
                "published_at": "2024-01-15T10:00:00Z",
                "body": "Release notes here",
                "html_url": "https://github.com/test/releases/tag/v1.2.3"
              }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Has.Count.EqualTo(1));
        Assert.That(result.Releases[0].Version, Is.EqualTo("1.2.3"));
        Assert.That(result.Releases[0].Body, Is.EqualTo("Release notes here"));
        Assert.That(result.Releases[0].Url, Is.EqualTo("https://github.com/test/releases/tag/v1.2.3"));
    }

    [Test]
    public void CheckForUpdate_should_strip_v_prefix_from_tag_name()
    {
        var json = """
            [
              {
                "tag_name": "v4.5.6",
                "draft": false,
                "published_at": "2024-01-01T00:00:00Z",
                "body": "test",
                "html_url": "https://github.com/test/releases/tag/v4.5.6"
              }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases[0].Version, Is.EqualTo("4.5.6"));
        Assert.That(result.LatestVersion, Is.EqualTo("4.5.6"));
    }

    [Test]
    public void CheckForUpdate_should_skip_draft_releases()
    {
        var json = """
            [
              {
                "tag_name": "v2.0.0",
                "draft": true,
                "published_at": "2024-02-01T00:00:00Z",
                "body": "Draft release",
                "html_url": "https://github.com/test/releases/tag/v2.0.0"
              }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Is.Empty);
        Assert.That(result.LatestVersion, Is.Null);
    }

    [Test]
    public void CheckForUpdate_should_skip_releases_with_invalid_version_tag()
    {
        var json = """
            [
              {
                "tag_name": "not-a-version",
                "draft": false,
                "published_at": "2024-01-01T00:00:00Z",
                "body": "Bad tag",
                "html_url": "https://github.com/test/releases/tag/bad"
              }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Is.Empty);
    }

    [Test]
    public void CheckForUpdate_should_set_latest_version_to_highest_among_multiple_releases()
    {
        var json = """
            [
              {
                "tag_name": "v1.0.0",
                "draft": false,
                "published_at": "2024-01-01T00:00:00Z",
                "body": "First",
                "html_url": "https://github.com/test/releases/tag/v1.0.0"
              },
              {
                "tag_name": "v3.0.0",
                "draft": false,
                "published_at": "2024-03-01T00:00:00Z",
                "body": "Third",
                "html_url": "https://github.com/test/releases/tag/v3.0.0"
              },
              {
                "tag_name": "v2.0.0",
                "draft": false,
                "published_at": "2024-02-01T00:00:00Z",
                "body": "Second",
                "html_url": "https://github.com/test/releases/tag/v2.0.0"
              }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Has.Count.EqualTo(3));
        Assert.That(result.LatestVersion, Is.EqualTo("3.0.0"));
    }

    [Test]
    public void CheckForUpdate_should_set_update_available_true_when_github_has_higher_version()
    {
        // Version 999.0.0 is guaranteed to be newer than the assembly/test version.
        var json = """
            [
              {
                "tag_name": "v999.0.0",
                "draft": false,
                "published_at": "2024-01-01T00:00:00Z",
                "body": "Future release",
                "html_url": "https://github.com/test/releases/tag/v999.0.0"
              }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.UpdateAvailable, Is.True);
        Assert.That(result.LatestVersion, Is.EqualTo("999.0.0"));
    }

    [Test]
    public void CheckForUpdate_should_parse_published_at_date_from_release()
    {
        var json = """
            [
              {
                "tag_name": "v1.5.0",
                "draft": false,
                "published_at": "2024-06-15T12:30:00Z",
                "body": "Test release",
                "html_url": "https://github.com/test/releases/tag/v1.5.0"
              }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases[0].PublishedAt.Year, Is.EqualTo(2024));
        Assert.That(result.Releases[0].PublishedAt.Month, Is.EqualTo(6));
        Assert.That(result.Releases[0].PublishedAt.Day, Is.EqualTo(15));
    }

    [Test]
    public void CheckForUpdate_should_include_current_version_in_successful_response()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.CurrentVersion, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void CheckForUpdate_should_include_current_version_in_fallback_response()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "");
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.CurrentVersion, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void CheckForUpdate_should_mix_valid_and_invalid_tags_correctly()
    {
        var json = """
            [
              {
                "tag_name": "v1.0.0",
                "draft": false,
                "published_at": "2024-01-01T00:00:00Z",
                "body": "Valid",
                "html_url": "https://github.com/test/releases/tag/v1.0.0"
              },
              {
                "tag_name": "bad-tag",
                "draft": false,
                "published_at": "2024-01-02T00:00:00Z",
                "body": "Invalid tag",
                "html_url": "https://github.com/test/releases/tag/bad"
              },
              {
                "tag_name": "v2.0.0",
                "draft": true,
                "published_at": "2024-01-03T00:00:00Z",
                "body": "Draft",
                "html_url": "https://github.com/test/releases/tag/v2.0.0"
              }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        // Only the first entry (v1.0.0) passes all filters.
        Assert.That(result.Releases, Has.Count.EqualTo(1));
        Assert.That(result.Releases[0].Version, Is.EqualTo("1.0.0"));
    }
}
