using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
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
        return (UpdateInfo)method.Invoke(null, new object[] { currentVersion?.ToString(), latestVersion?.ToString(), releases });
    }

    private static UpdateInfo InvokeBuildResult(string currentVersion, string latestVersion, List<ReleaseInfo> releases)
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

    private static UpdateService CreateWithHandler(
        MockHttpMessageHandler handler,
        IConfigService configService = null,
        IUpdatePackageProvider packageProvider = null)
    {
        return new UpdateService(new HttpClient(handler), null, configService, packageProvider);
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

    // --- SemVersion tests ---

    [Test]
    public void SemVersion_TryParse_should_parse_prerelease_and_build_metadata()
    {
        var success = SemVersion.TryParse("v1.5.0-rc.2+build.123", out var semVer);

        Assert.That(success, Is.True);
        Assert.That(semVer.Major, Is.EqualTo(1));
        Assert.That(semVer.Minor, Is.EqualTo(5));
        Assert.That(semVer.Patch, Is.EqualTo(0));
        Assert.That(semVer.Prerelease, Is.EqualTo("rc.2"));
        Assert.That(semVer.BuildMetadata, Is.EqualTo("build.123"));
        Assert.That(semVer.IsPrerelease, Is.True);
    }

    [Test]
    public void SemVersion_precedence_release_higher_than_prerelease()
    {
        Assert.That(SemVersion.Parse("1.0.0") > SemVersion.Parse("1.0.0-rc1"), Is.True);
        Assert.That(SemVersion.Parse("1.0.0-rc1") < SemVersion.Parse("1.0.0"), Is.True);
    }

    [Test]
    public void SemVersion_precedence_higher_prerelease_wins()
    {
        Assert.That(SemVersion.Parse("1.0.0-rc2") > SemVersion.Parse("1.0.0-rc1"), Is.True);
        Assert.That(SemVersion.Parse("1.0.0-rc.10") > SemVersion.Parse("1.0.0-rc.2"), Is.True);
        Assert.That(SemVersion.Parse("1.0.0-rc10") > SemVersion.Parse("1.0.0-rc2"), Is.True);
        Assert.That(SemVersion.Parse("1.0.0-beta.1") < SemVersion.Parse("1.0.0-rc.1"), Is.True);
    }

    [Test]
    public void SemVersion_precedence_higher_core_wins()
    {
        Assert.That(SemVersion.Parse("1.5.0-rc1") > SemVersion.Parse("1.4.9"), Is.True);
        Assert.That(SemVersion.Parse("2.0.0-alpha") > SemVersion.Parse("1.9.9"), Is.True);
    }

    [Test]
    public void SemVersion_precedence_ignores_build_metadata()
    {
        Assert.That(SemVersion.Parse("1.0.0+build1") == SemVersion.Parse("1.0.0+build2"), Is.True);
    }

    // --- GitHub Release Prerelease parsing tests ---

    [Test]
    public void CheckForUpdate_should_not_drop_prerelease_versions_from_releases_list()
    {
        var json = """
            [
                {
                    "tag_name": "v1.5.0-rc1",
                    "draft": false,
                    "published_at": "2024-06-01T00:00:00Z",
                    "body": "RC1 release",
                    "html_url": "https://github.com/test/releases/tag/v1.5.0-rc1"
                },
                {
                    "tag_name": "v1.4.0",
                    "draft": false,
                    "published_at": "2024-05-01T00:00:00Z",
                    "body": "Stable release",
                    "html_url": "https://github.com/test/releases/tag/v1.4.0"
                }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Has.Count.EqualTo(2));
        Assert.That(result.Releases[0].Version, Is.EqualTo("1.5.0-rc1"));
        Assert.That(result.LatestVersion, Is.EqualTo("1.5.0-rc1"));
    }

    [Test]
    public void CheckForUpdate_should_retain_semver_2_prereleases_with_dot_identifiers_and_build_metadata()
    {
        var json = """
            [
                {
                    "tag_name": "v2.0.0-rc.2+build.42",
                    "draft": false,
                    "published_at": "2024-07-01T00:00:00Z",
                    "body": "RC2 release",
                    "html_url": "https://github.com/test/releases/tag/v2.0.0-rc.2"
                },
                {
                    "tag_name": "v1.6.0-beta.2",
                    "draft": false,
                    "published_at": "2024-06-15T00:00:00Z",
                    "body": "Beta release",
                    "html_url": "https://github.com/test/releases/tag/v1.6.0-beta.2"
                }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Has.Count.EqualTo(2));
        Assert.That(result.Releases[0].Version, Is.EqualTo("2.0.0-rc.2+build.42"));
        Assert.That(result.Releases[1].Version, Is.EqualTo("1.6.0-beta.2"));
        Assert.That(result.LatestVersion, Is.EqualTo("2.0.0-rc.2+build.42"));
    }

    [Test]
    public void BuildResult_should_set_update_available_true_for_prerelease_to_full_release()
    {
        var result = InvokeBuildResult("1.5.0-rc1", "1.5.0", new List<ReleaseInfo>());

        Assert.That(result.UpdateAvailable, Is.True);
    }

    [Test]
    public void BuildResult_should_set_update_available_false_for_full_release_to_prerelease()
    {
        var result = InvokeBuildResult("1.5.0", "1.5.0-rc1", new List<ReleaseInfo>());

        Assert.That(result.UpdateAvailable, Is.False);
    }

    // --- Changelog Markdown prerelease parsing tests ---

    [Test]
    public void ParseChangelogMarkdown_should_parse_prerelease_version_headers()
    {
        var changelog = """
            # Changelog

            ## [v1.5.0-rc1](https://github.com/dmzoneill/Seedarr/releases/tag/v1.5.0-rc1) - 2026-09-16

            ### ✨ Features
            - feat: add prerelease support

            ## [1.4.0-beta.2] - 2026-09-01

            ### 🐛 Bug Fixes
            - fix: beta fix
            """;

        var releases = UpdateService.ParseChangelogMarkdown(changelog);

        Assert.That(releases, Has.Count.EqualTo(2));
        Assert.That(releases[0].Version, Is.EqualTo("1.5.0-rc1"));
        Assert.That(releases[0].Body, Does.Contain("feat: add prerelease support"));
        Assert.That(releases[1].Version, Is.EqualTo("1.4.0-beta.2"));
        Assert.That(releases[1].Body, Does.Contain("fix: beta fix"));
    }

    [Test]
    public void ParseChangelogMarkdown_header_with_date_in_parentheses_sets_published_at_and_fallback_url()
    {
        var changelog = """
            # Changelog

            ## 1.2.0 (2026-09-01)

            - Initial release
            """;

        var releases = UpdateService.ParseChangelogMarkdown(changelog);

        Assert.That(releases, Has.Count.EqualTo(1));
        Assert.That(releases[0].Version, Is.EqualTo("1.2.0"));
        Assert.That(releases[0].PublishedAt.Year, Is.EqualTo(2026));
        Assert.That(releases[0].PublishedAt.Month, Is.EqualTo(9));
        Assert.That(releases[0].PublishedAt.Day, Is.EqualTo(1));
        Assert.That(releases[0].Url, Is.EqualTo("https://github.com/dmzoneill/Seedarr/releases/tag/v1.2.0"));
        Assert.That(releases[0].Url, Is.Not.EqualTo("2026-09-01"));
    }

    [Test]
    public void ParseChangelogMarkdown_header_with_brackets_and_date_in_parentheses_sets_published_at_and_fallback_url()
    {
        var changelog = """
            # Changelog

            ## [1.2.0] (2026-09-01)

            - Initial release
            """;

        var releases = UpdateService.ParseChangelogMarkdown(changelog);

        Assert.That(releases, Has.Count.EqualTo(1));
        Assert.That(releases[0].Version, Is.EqualTo("1.2.0"));
        Assert.That(releases[0].PublishedAt.Year, Is.EqualTo(2026));
        Assert.That(releases[0].PublishedAt.Month, Is.EqualTo(9));
        Assert.That(releases[0].PublishedAt.Day, Is.EqualTo(1));
        Assert.That(releases[0].Url, Is.EqualTo("https://github.com/dmzoneill/Seedarr/releases/tag/v1.2.0"));
    }

    [Test]
    public void ParseChangelogMarkdown_header_with_url_in_parentheses_sets_url()
    {
        var changelog = """
            # Changelog

            ## 1.2.0 (https://github.com/dmzoneill/Seedarr/releases/tag/v1.2.0)

            - Initial release
            """;

        var releases = UpdateService.ParseChangelogMarkdown(changelog);

        Assert.That(releases, Has.Count.EqualTo(1));
        Assert.That(releases[0].Version, Is.EqualTo("1.2.0"));
        Assert.That(releases[0].Url, Is.EqualTo("https://github.com/dmzoneill/Seedarr/releases/tag/v1.2.0"));
    }

    [Test]
    public void ParseChangelogMarkdown_header_with_url_and_hyphenated_date_sets_url_and_published_at()
    {
        var changelog = """
            # Changelog

            ## 1.2.0 (https://github.com/dmzoneill/Seedarr/releases/tag/v1.2.0) - 2026-09-01

            - Initial release
            """;

        var releases = UpdateService.ParseChangelogMarkdown(changelog);

        Assert.That(releases, Has.Count.EqualTo(1));
        Assert.That(releases[0].Version, Is.EqualTo("1.2.0"));
        Assert.That(releases[0].Url, Is.EqualTo("https://github.com/dmzoneill/Seedarr/releases/tag/v1.2.0"));
        Assert.That(releases[0].PublishedAt.Year, Is.EqualTo(2026));
        Assert.That(releases[0].PublishedAt.Month, Is.EqualTo(9));
        Assert.That(releases[0].PublishedAt.Day, Is.EqualTo(1));
    }

    // --- Container detection tests ---

    [Test]
    public void IsRunningInContainer_respects_environment_variable()
    {
        var prev = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
            Assert.That(UpdateService.IsRunningInContainer(), Is.True);

            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "false");
            // If /.dockerenv does not exist on test machine, it should be false
            if (!System.IO.File.Exists("/.dockerenv"))
            {
                Assert.That(UpdateService.IsRunningInContainer(), Is.False);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", prev);
        }
    }

    [Test]
    public void BuildResult_should_set_is_containerized()
    {
        var result = InvokeBuildResult("1.0.0", "1.1.0", new List<ReleaseInfo>());
        Assert.That(result.IsContainerized, Is.EqualTo(UpdateService.IsRunningInContainer()));
    }

    // --- Issue #187 Rate Limiting, Changelog Fallback, and Prerelease Tests ---

    [Test]
    public void CheckForUpdate_should_set_cache_expiry_from_x_ratelimit_reset_header_on_403()
    {
        var resetTime = DateTime.UtcNow.AddMinutes(45);
        var resetEpoch = new DateTimeOffset(resetTime).ToUnixTimeSeconds();
        var handler = new MockHttpMessageHandler();
        handler.EnqueueWithHeaders(
            HttpStatusCode.Forbidden,
            "{\"message\": \"API rate limit exceeded\"}",
            new Dictionary<string, string>
            {
                ["x-ratelimit-reset"] = resetEpoch.ToString(),
                ["x-ratelimit-remaining"] = "0"
            });
        var subject = CreateWithHandler(handler);

        subject.CheckForUpdate();

        var expiryField = typeof(UpdateService).GetField("_cacheExpiry", BindingFlags.NonPublic | BindingFlags.Instance);
        var expiry = (DateTime)expiryField.GetValue(subject);

        Assert.That(Math.Abs((expiry - DateTimeOffset.FromUnixTimeSeconds(resetEpoch).UtcDateTime).TotalSeconds), Is.LessThan(2));
    }

    [Test]
    public void CheckForUpdate_should_set_cache_expiry_from_retry_after_header_on_429()
    {
        var handler = new MockHttpMessageHandler();
        handler.EnqueueWithHeaders(
            (HttpStatusCode)429,
            "{\"message\": \"Too Many Requests\"}",
            new Dictionary<string, string>
            {
                ["retry-after"] = "1800"
            });
        var subject = CreateWithHandler(handler);

        var before = DateTime.UtcNow;
        subject.CheckForUpdate();
        var after = DateTime.UtcNow;

        var expiryField = typeof(UpdateService).GetField("_cacheExpiry", BindingFlags.NonPublic | BindingFlags.Instance);
        var expiry = (DateTime)expiryField.GetValue(subject);

        Assert.That(expiry, Is.GreaterThanOrEqualTo(before.AddSeconds(1800)));
        Assert.That(expiry, Is.LessThanOrEqualTo(after.AddSeconds(1805)));
    }

    [Test]
    public void CheckForUpdate_should_default_cache_expiry_to_30_minutes_on_403_without_headers()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, "Forbidden");
        var subject = CreateWithHandler(handler);

        var before = DateTime.UtcNow;
        subject.CheckForUpdate();
        var after = DateTime.UtcNow;

        var expiryField = typeof(UpdateService).GetField("_cacheExpiry", BindingFlags.NonPublic | BindingFlags.Instance);
        var expiry = (DateTime)expiryField.GetValue(subject);

        Assert.That(expiry, Is.GreaterThanOrEqualTo(before.AddMinutes(29)));
        Assert.That(expiry, Is.LessThanOrEqualTo(after.AddMinutes(31)));
    }

    [Test]
    public void CheckForUpdate_should_default_cache_expiry_to_30_minutes_on_network_failure()
    {
        var subject = CreateWithThrowingHandler(new HttpRequestException("network down"));

        var before = DateTime.UtcNow;
        subject.CheckForUpdate();
        var after = DateTime.UtcNow;

        var expiryField = typeof(UpdateService).GetField("_cacheExpiry", BindingFlags.NonPublic | BindingFlags.Instance);
        var expiry = (DateTime)expiryField.GetValue(subject);

        Assert.That(expiry, Is.GreaterThanOrEqualTo(before.AddMinutes(29)));
        Assert.That(expiry, Is.LessThanOrEqualTo(after.AddMinutes(31)));
    }

    [Test]
    public void CheckForUpdate_should_preserve_last_known_valid_releases_on_403_rate_limit()
    {
        var handler = new MockHttpMessageHandler();
        var validJson = """
            [
                {
                    "tag_name": "v2.0.0",
                    "draft": false,
                    "published_at": "2024-05-01T00:00:00Z",
                    "body": "Valid release",
                    "html_url": "https://github.com/test/releases/tag/v2.0.0"
                }
            ]
            """;
        handler.Enqueue(HttpStatusCode.OK, validJson);
        var subject = CreateWithHandler(handler);

        var firstResult = subject.CheckForUpdate();
        Assert.That(firstResult.Releases, Has.Count.EqualTo(1));

        var expiryField = typeof(UpdateService).GetField("_cacheExpiry", BindingFlags.NonPublic | BindingFlags.Instance);
        expiryField.SetValue(subject, DateTime.UtcNow.AddMinutes(-5));

        var resetTime = DateTime.UtcNow.AddMinutes(45);
        var resetEpoch = new DateTimeOffset(resetTime).ToUnixTimeSeconds();
        handler.EnqueueWithHeaders(
            HttpStatusCode.Forbidden,
            "{\"message\": \"API rate limit exceeded\"}",
            new Dictionary<string, string>
            {
                ["x-ratelimit-reset"] = resetEpoch.ToString()
            });

        var secondResult = subject.CheckForUpdate();

        Assert.That(secondResult.Releases, Has.Count.EqualTo(1));
        Assert.That(secondResult.Releases[0].Version, Is.EqualTo("2.0.0"));
        var newExpiry = (DateTime)expiryField.GetValue(subject);
        Assert.That(newExpiry, Is.GreaterThan(DateTime.UtcNow.AddMinutes(30)));
    }

    [Test]
    public void CheckForUpdate_should_preserve_last_known_valid_releases_on_network_failure()
    {
        var handler = new MockHttpMessageHandler();
        var validJson = """
            [
                {
                    "tag_name": "v3.0.0",
                    "draft": false,
                    "published_at": "2024-05-01T00:00:00Z",
                    "body": "Valid release",
                    "html_url": "https://github.com/test/releases/tag/v3.0.0"
                }
            ]
            """;
        handler.Enqueue(HttpStatusCode.OK, validJson);
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "Service Unavailable");
        var subject = CreateWithHandler(handler);

        var firstResult = subject.CheckForUpdate();
        Assert.That(firstResult.Releases, Has.Count.EqualTo(1));

        var expiryField = typeof(UpdateService).GetField("_cacheExpiry", BindingFlags.NonPublic | BindingFlags.Instance);
        expiryField.SetValue(subject, DateTime.UtcNow.AddMinutes(-5));

        var secondResult = subject.CheckForUpdate();

        Assert.That(secondResult.Releases, Has.Count.EqualTo(1));
        Assert.That(secondResult.Releases[0].Version, Is.EqualTo("3.0.0"));
    }

    [Test]
    public void SemVersion_TryParse_should_parse_prerelease_without_dash()
    {
        var success1 = SemVersion.TryParse("v1.6.7beta1", out var v1);
        Assert.That(success1, Is.True);
        Assert.That(v1.Major, Is.EqualTo(1));
        Assert.That(v1.Minor, Is.EqualTo(6));
        Assert.That(v1.Patch, Is.EqualTo(7));
        Assert.That(v1.Prerelease, Is.EqualTo("beta1"));

        var success2 = SemVersion.TryParse("v2.0.0rc2", out var v2);
        Assert.That(success2, Is.True);
        Assert.That(v2.Major, Is.EqualTo(2));
        Assert.That(v2.Minor, Is.EqualTo(0));
        Assert.That(v2.Patch, Is.EqualTo(0));
        Assert.That(v2.Prerelease, Is.EqualTo("rc2"));
    }

    [Test]
    public void CheckForUpdate_should_parse_prereleases_without_dash_and_not_discard_them()
    {
        var json = """
            [
                {
                    "tag_name": "v1.6.7beta1",
                    "draft": false,
                    "published_at": "2024-06-01T00:00:00Z",
                    "body": "Beta release",
                    "html_url": "https://github.com/test/releases/tag/v1.6.7beta1"
                },
                {
                    "tag_name": "v2.0.0rc1",
                    "draft": false,
                    "published_at": "2024-07-01T00:00:00Z",
                    "body": "RC release",
                    "html_url": "https://github.com/test/releases/tag/v2.0.0rc1"
                }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var subject = CreateWithHandler(handler);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Has.Count.EqualTo(2));
        Assert.That(result.Releases[0].Version, Is.EqualTo("1.6.7beta1"));
        Assert.That(result.Releases[1].Version, Is.EqualTo("2.0.0rc1"));
        Assert.That(result.LatestVersion, Is.EqualTo("2.0.0rc1"));
    }

    [Test]
    public void GetLatestVersion_should_extract_core_version_for_prerelease()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "1.6.7-beta1",
            Releases = new List<ReleaseInfo>(),
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(5));

        var result = _subject.GetLatestVersion();

        Assert.That(result, Is.EqualTo(new Version(1, 6, 7)));
    }

    [Test]
    public void CheckForUpdate_should_invoke_changelog_fallback_when_remote_fetch_fails()
    {
        var changelogReleases = new List<ReleaseInfo>
        {
            new() { Version = "1.5.0", Body = "Local changelog release", PublishedAt = DateTime.UtcNow }
        };
        UpdateService.ChangelogProvider = () => changelogReleases;

        try
        {
            var handler = new MockHttpMessageHandler();
            handler.Enqueue(HttpStatusCode.ServiceUnavailable, "Service Unavailable");
            var subject = CreateWithHandler(handler);

            var result = subject.CheckForUpdate();

            Assert.That(result.Releases, Has.Count.EqualTo(1));
            Assert.That(result.Releases[0].Version, Is.EqualTo("1.5.0"));
            Assert.That(result.LatestVersion, Is.EqualTo("1.5.0"));
        }
        finally
        {
            UpdateService.ChangelogProvider = null;
        }
    }

    [Test]
    public void CheckForUpdate_should_invoke_changelog_fallback_on_rate_limit_when_no_cached_releases()
    {
        var changelogReleases = new List<ReleaseInfo>
        {
            new() { Version = "1.4.0", Body = "Changelog release", PublishedAt = DateTime.UtcNow }
        };
        UpdateService.ChangelogProvider = () => changelogReleases;

        try
        {
            var handler = new MockHttpMessageHandler();
            handler.Enqueue(HttpStatusCode.Forbidden, "Rate limit");
            var subject = CreateWithHandler(handler);

            var result = subject.CheckForUpdate();

            Assert.That(result.Releases, Has.Count.EqualTo(1));
            Assert.That(result.Releases[0].Version, Is.EqualTo("1.4.0"));
        }
        finally
        {
            UpdateService.ChangelogProvider = null;
        }
    }

    [Test]
    public async Task CheckForUpdateAsync_should_invoke_changelog_fallback_when_remote_fetch_fails()
    {
        var changelogReleases = new List<ReleaseInfo>
        {
            new() { Version = "1.7.0", Body = "Async changelog release", PublishedAt = DateTime.UtcNow }
        };
        UpdateService.ChangelogProvider = () => changelogReleases;

        try
        {
            var handler = new MockHttpMessageHandler();
            handler.Enqueue(HttpStatusCode.InternalServerError, "Error");
            var subject = CreateWithHandler(handler);

            var result = await subject.CheckForUpdateAsync();

            Assert.That(result.Releases, Has.Count.EqualTo(1));
            Assert.That(result.Releases[0].Version, Is.EqualTo("1.7.0"));
        }
        finally
        {
            UpdateService.ChangelogProvider = null;
        }
    }

    [Test]
    public void CachedUpdateInfo_should_return_cached_instance_without_network_call()
    {
        var cached = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "2.0.0",
            UpdateAvailable = true
        };
        SetCachedResult(cached, DateTime.UtcNow.AddHours(1));

        Assert.That(_subject.CachedUpdateInfo, Is.SameAs(cached));
    }

    [Test]
    public void IsCacheExpired_should_return_true_when_expired_or_null()
    {
        SetCachedResult(null, DateTime.MinValue);
        Assert.That(_subject.IsCacheExpired, Is.True);

        var cached = new UpdateInfo();
        SetCachedResult(cached, DateTime.UtcNow.AddMinutes(-10));
        Assert.That(_subject.IsCacheExpired, Is.True);

        SetCachedResult(cached, DateTime.UtcNow.AddMinutes(10));
        Assert.That(_subject.IsCacheExpired, Is.False);
    }

    [Test]
    public void CheckForUpdate_should_exclude_prerelease_on_stable_channel()
    {
        var json = """
            [
                {
                    "tag_name": "v2.0.0-beta.1",
                    "draft": false,
                    "prerelease": true,
                    "published_at": "2024-02-01T00:00:00Z",
                    "body": "Beta release",
                    "html_url": "https://github.com/test/releases/tag/v2.0.0-beta.1"
                },
                {
                    "tag_name": "v1.9.0",
                    "draft": false,
                    "prerelease": false,
                    "published_at": "2024-01-01T00:00:00Z",
                    "body": "Stable release",
                    "html_url": "https://github.com/test/releases/tag/v1.9.0"
                }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);

        var config = Substitute.For<IConfigService>();
        config.UpdateBranch.Returns("stable");

        var subject = CreateWithHandler(handler, config);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Has.Count.EqualTo(1));
        Assert.That(result.Releases[0].Version, Is.EqualTo("1.9.0"));
        Assert.That(result.LatestVersion, Is.EqualTo("1.9.0"));
    }

    [Test]
    public void CheckForUpdate_should_include_prerelease_on_develop_channel()
    {
        var json = """
            [
                {
                    "tag_name": "v2.0.0-beta.1",
                    "draft": false,
                    "prerelease": true,
                    "published_at": "2024-02-01T00:00:00Z",
                    "body": "Beta release",
                    "html_url": "https://github.com/test/releases/tag/v2.0.0-beta.1"
                },
                {
                    "tag_name": "v1.9.0",
                    "draft": false,
                    "prerelease": false,
                    "published_at": "2024-01-01T00:00:00Z",
                    "body": "Stable release",
                    "html_url": "https://github.com/test/releases/tag/v1.9.0"
                }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);

        var config = Substitute.For<IConfigService>();
        config.UpdateBranch.Returns("develop");

        var subject = CreateWithHandler(handler, config);

        var result = subject.CheckForUpdate();

        Assert.That(result.Releases, Has.Count.EqualTo(2));
        Assert.That(result.LatestVersion, Is.EqualTo("2.0.0-beta.1"));
    }

    [Test]
    public void CheckForUpdate_should_resolve_package_and_checksum_details()
    {
        var json = """
            [
                {
                    "tag_name": "v1.5.0",
                    "draft": false,
                    "prerelease": false,
                    "published_at": "2024-01-01T00:00:00Z",
                    "body": "Stable release",
                    "html_url": "https://github.com/test/releases/tag/v1.5.0",
                    "assets": [
                        {
                            "name": "seedarr-linux-x64.tar.gz",
                            "browser_download_url": "https://github.com/test/download/seedarr-linux-x64.tar.gz",
                            "size": 52428800
                        },
                        {
                            "name": "seedarr-linux-x64.tar.gz.sha256",
                            "browser_download_url": "https://github.com/test/download/seedarr-linux-x64.tar.gz.sha256",
                            "size": 64
                        }
                    ]
                }
            ]
            """;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);

        var packageProvider = new UpdatePackageProvider(platformOverride: "linux-x64");
        var subject = CreateWithHandler(handler, packageProvider: packageProvider);

        var result = subject.CheckForUpdate();

        Assert.That(result.Package, Is.Not.Null);
        Assert.That(result.Package.FileName, Is.EqualTo("seedarr-linux-x64.tar.gz"));
        Assert.That(result.Package.DownloadUrl, Is.EqualTo("https://github.com/test/download/seedarr-linux-x64.tar.gz"));
        Assert.That(result.Package.Sha256ChecksumUrl, Is.EqualTo("https://github.com/test/download/seedarr-linux-x64.tar.gz.sha256"));
        Assert.That(result.PackageUrl, Is.EqualTo("https://github.com/test/download/seedarr-linux-x64.tar.gz"));
        Assert.That(result.PackageFileName, Is.EqualTo("seedarr-linux-x64.tar.gz"));
    }
}
