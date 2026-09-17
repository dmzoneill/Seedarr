using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Test.RemotePathMappings;

[TestFixture]
public class RemotePathMappingServiceTest
{
    private RemotePathMappingService _service;

    [SetUp]
    public void Setup()
    {
        _service = new RemotePathMappingService();
    }

    [Test]
    public void Remap_returns_original_when_path_is_null_or_empty()
    {
        Assert.That(_service.Remap("localhost", null), Is.Null);
        Assert.That(_service.Remap("localhost", string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Remap_returns_original_when_host_is_null_or_whitespace()
    {
        Assert.That(_service.Remap(null, "/remote/path"), Is.EqualTo("/remote/path"));
        Assert.That(_service.Remap("   ", "/remote/path"), Is.EqualTo("/remote/path"));
    }

    [Test]
    public void Remap_returns_original_when_no_mappings_exist()
    {
        var result = _service.Remap("host1", "/remote/path/file.mkv");
        Assert.That(result, Is.EqualTo("/remote/path/file.mkv"));
    }

    [Test]
    public void Remap_returns_original_when_host_does_not_match()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "host1",
            RemotePath = "/downloads",
            LocalPath = "/mnt/downloads"
        });

        var result = _service.Remap("host2", "/downloads/movie.mkv");
        Assert.That(result, Is.EqualTo("/downloads/movie.mkv"));
    }

    [Test]
    public void Remap_matches_host_case_insensitively_and_trims()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "my-host.local",
            RemotePath = "/downloads",
            LocalPath = "/mnt/downloads"
        });

        var result = _service.Remap(" MY-HOST.LOCAL ", "/downloads/movie.mkv");
        Assert.That(result, Is.EqualTo("/mnt/downloads/movie.mkv"));
    }

    [Test]
    public void Remap_exact_match_returns_local_path()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "192.168.1.100",
            RemotePath = "/var/lib/transmission/downloads",
            LocalPath = "/media/downloads"
        });

        var result = _service.Remap("192.168.1.100", "/var/lib/transmission/downloads");
        Assert.That(result, Is.EqualTo("/media/downloads"));
    }

    [Test]
    public void Remap_subpath_replaces_prefix_correctly()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "remoteclient",
            RemotePath = "/data/torrents",
            LocalPath = "/mnt/storage/torrents"
        });

        var result = _service.Remap("remoteclient", "/data/torrents/completed/series/s01e01.mkv");
        Assert.That(result, Is.EqualTo("/mnt/storage/torrents/completed/series/s01e01.mkv"));
    }

    [Test]
    public void Remap_translates_windows_remote_to_linux_local()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "qbit-win",
            RemotePath = @"D:\Downloads\Torrents",
            LocalPath = "/data/downloads"
        });

        var result = _service.Remap("qbit-win", @"D:\Downloads\Torrents\Movie (2024)\movie.mkv");
        Assert.That(result, Is.EqualTo("/data/downloads/Movie (2024)/movie.mkv"));
    }

    [Test]
    public void Remap_translates_linux_remote_to_windows_local()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/home/seedbox/downloads",
            LocalPath = @"C:\Media\Seedbox"
        });

        var result = _service.Remap("seedbox", "/home/seedbox/downloads/linux/iso.img");
        Assert.That(result, Is.EqualTo(@"C:\Media\Seedbox\linux\iso.img"));
    }

    [Test]
    public void Remap_preserves_trailing_slash()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/remote/downloads",
            LocalPath = "/local/downloads"
        });

        var result = _service.Remap("seedbox", "/remote/downloads/subfolder/");
        Assert.That(result, Is.EqualTo("/local/downloads/subfolder/"));
    }

    [Test]
    public void Remap_selects_longest_remote_path_prefix_when_multiple_match()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/media",
            LocalPath = "/mnt/storage"
        });

        _service.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/media/downloads/torrents",
            LocalPath = "/mnt/storage/fast-torrents"
        });

        _service.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/media/downloads",
            LocalPath = "/mnt/storage/downloads"
        });

        var result = _service.Remap("seedbox", "/media/downloads/torrents/seeding/file.dat");
        Assert.That(result, Is.EqualTo("/mnt/storage/fast-torrents/seeding/file.dat"));

        var result2 = _service.Remap("seedbox", "/media/downloads/other/file.dat");
        Assert.That(result2, Is.EqualTo("/mnt/storage/downloads/other/file.dat"));

        var result3 = _service.Remap("seedbox", "/media/general/file.dat");
        Assert.That(result3, Is.EqualTo("/mnt/storage/general/file.dat"));
    }

    [Test]
    public void Remap_ignores_partial_directory_name_match()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/data/downloads",
            LocalPath = "/mnt/downloads"
        });

        // /data/downloads-extra should not match /data/downloads
        var result = _service.Remap("seedbox", "/data/downloads-extra/file.dat");
        Assert.That(result, Is.EqualTo("/data/downloads-extra/file.dat"));
    }

    [Test]
    public void Crud_operations_work_correctly()
    {
        var mapping1 = _service.Add(new RemotePathMapping
        {
            Host = "host1",
            RemotePath = "/remote1",
            LocalPath = "/local1"
        });

        Assert.That(mapping1.Id, Is.GreaterThan(0));

        var retrieved = _service.Get(mapping1.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Host, Is.EqualTo("host1"));

        var all = _service.All();
        Assert.That(all, Has.Count.EqualTo(1));

        mapping1.Host = "host1-updated";
        _service.Update(mapping1);

        var updated = _service.Get(mapping1.Id);
        Assert.That(updated.Host, Is.EqualTo("host1-updated"));

        _service.Delete(mapping1.Id);
        Assert.That(_service.Get(mapping1.Id), Is.Null);
        Assert.That(_service.All(), Is.Empty);
    }

    [Test]
    public void TestMapping_with_matching_rule_verifies_mapped_path_prefix_replacement_and_rule_applied()
    {
        var mapping = _service.Add(new RemotePathMapping
        {
            Host = "seedbox.local",
            RemotePath = "/remote/torrents/complete",
            LocalPath = "/media/storage/downloads"
        });

        var result = _service.TestMapping("seedbox.local", "/remote/torrents/complete/movie/sample.mkv");

        Assert.That(result.RuleApplied, Is.True);
        Assert.That(result.MappedPath, Is.EqualTo("/media/storage/downloads/movie/sample.mkv"));
        Assert.That(result.InputPath, Is.EqualTo("/remote/torrents/complete/movie/sample.mkv"));
        Assert.That(result.MatchedRuleId, Is.EqualTo(mapping.Id));
        Assert.That(result.MatchedRuleHost, Is.EqualTo("seedbox.local"));
        Assert.That(result.MatchedRemotePrefix, Is.EqualTo("/remote/torrents/complete"));
        Assert.That(result.MatchedLocalPrefix, Is.EqualTo("/media/storage/downloads"));
    }

    [Test]
    public void TestMapping_with_non_matching_host_or_path_returns_rule_applied_false()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "seedbox.local",
            RemotePath = "/remote/torrents",
            LocalPath = "/media/torrents"
        });

        var resultNonMatchingHost = _service.TestMapping("otherhost.local", "/remote/torrents/file.mkv");
        Assert.That(resultNonMatchingHost.RuleApplied, Is.False);
        Assert.That(resultNonMatchingHost.MappedPath, Is.EqualTo("/remote/torrents/file.mkv"));
        Assert.That(resultNonMatchingHost.MatchedRuleId, Is.Null);

        var resultNonMatchingPath = _service.TestMapping("seedbox.local", "/other/path/file.mkv");
        Assert.That(resultNonMatchingPath.RuleApplied, Is.False);
        Assert.That(resultNonMatchingPath.MappedPath, Is.EqualTo("/other/path/file.mkv"));
        Assert.That(resultNonMatchingPath.MatchedRuleId, Is.Null);
    }

    [Test]
    public void TestMapping_local_to_remote_direction_works_correctly()
    {
        var mapping = _service.Add(new RemotePathMapping
        {
            Host = "seedbox.local",
            RemotePath = "/remote/torrents",
            LocalPath = "/local/downloads"
        });

        var result = _service.TestMapping("seedbox.local", "/local/downloads/sub/file.mkv", "localToRemote");
        Assert.That(result.RuleApplied, Is.True);
        Assert.That(result.MappedPath, Is.EqualTo("/remote/torrents/sub/file.mkv"));
        Assert.That(result.MatchedRuleId, Is.EqualTo(mapping.Id));
    }

    [Test]
    public void IsPathPrefixMatch_enforces_segment_boundary_correctly()
    {
        // Boundary non-matches
        Assert.That(RemotePathMappingService.IsPathPrefixMatch("/downloads_movies/Avatar.mkv", "/downloads", StringComparison.Ordinal), Is.False);
        Assert.That(RemotePathMappingService.IsPathPrefixMatch(@"D:\Downloads_Extra\Avatar.mkv", @"D:\Downloads", StringComparison.OrdinalIgnoreCase), Is.False);

        // Boundary matches
        Assert.That(RemotePathMappingService.IsPathPrefixMatch("/downloads/Avatar.mkv", "/downloads", StringComparison.Ordinal), Is.True);
        Assert.That(RemotePathMappingService.IsPathPrefixMatch("/downloads", "/downloads", StringComparison.Ordinal), Is.True);
        Assert.That(RemotePathMappingService.IsPathPrefixMatch("/downloads/", "/downloads", StringComparison.Ordinal), Is.True);
        Assert.That(RemotePathMappingService.IsPathPrefixMatch("/downloads/Avatar.mkv", "/downloads/", StringComparison.Ordinal), Is.True);
        Assert.That(RemotePathMappingService.IsPathPrefixMatch(@"D:\Downloads\Avatar.mkv", @"D:\Downloads", StringComparison.OrdinalIgnoreCase), Is.True);
    }

    [Test]
    public void Remap_preserves_unc_paths_in_both_directions()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "nas.local",
            RemotePath = @"\\nas\share\downloads",
            LocalPath = "/mnt/storage/downloads"
        });

        // Windows UNC to Linux POSIX
        var toLinux = _service.Remap("nas.local", @"\\nas\share\downloads\movies\avatar.mkv");
        Assert.That(toLinux, Is.EqualTo("/mnt/storage/downloads/movies/avatar.mkv"));

        _service.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/home/seedbox/downloads",
            LocalPath = @"\\nas\share\seedbox"
        });

        // Linux POSIX to Windows UNC - must preserve leading double backslash
        var toUnc = _service.Remap("seedbox", "/home/seedbox/downloads/linux/iso.img");
        Assert.That(toUnc, Is.EqualTo(@"\\nas\share\seedbox\linux\iso.img"));
    }

    [Test]
    public void Remap_normalizes_cross_platform_separators_without_hybrid_slashes()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "win-client",
            RemotePath = @"D:\Torrents",
            LocalPath = "/data/torrents"
        });

        // Windows to Linux: all slashes in tail should be forward slashes
        var linuxResult = _service.Remap("win-client", @"D:\Torrents\Season 1\Episode 01\video.mkv");
        Assert.That(linuxResult, Is.EqualTo("/data/torrents/Season 1/Episode 01/video.mkv"));
        Assert.That(linuxResult.Contains('\\'), Is.False);

        _service.Add(new RemotePathMapping
        {
            Host = "linux-client",
            RemotePath = "/data/torrents",
            LocalPath = @"C:\Media\Torrents"
        });

        // Linux to Windows: all slashes in tail should be backslashes
        var winResult = _service.Remap("linux-client", "/data/torrents/Season 1/Episode 01/video.mkv");
        Assert.That(winResult, Is.EqualTo(@"C:\Media\Torrents\Season 1\Episode 01\video.mkv"));
        Assert.That(winResult.Contains('/'), Is.False);
    }

    [Test]
    public void Remap_trailing_slash_invariance_produces_identical_results()
    {
        var serviceWithTrailing = new RemotePathMappingService();
        serviceWithTrailing.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/remote/downloads/",
            LocalPath = "/local/downloads/"
        });

        var serviceWithoutTrailing = new RemotePathMappingService();
        serviceWithoutTrailing.Add(new RemotePathMapping
        {
            Host = "seedbox",
            RemotePath = "/remote/downloads",
            LocalPath = "/local/downloads"
        });

        // Subpath without trailing slash
        Assert.That(serviceWithTrailing.Remap("seedbox", "/remote/downloads/subfolder/file.mkv"),
            Is.EqualTo(serviceWithoutTrailing.Remap("seedbox", "/remote/downloads/subfolder/file.mkv")));
        Assert.That(serviceWithTrailing.Remap("seedbox", "/remote/downloads/subfolder/file.mkv"),
            Is.EqualTo("/local/downloads/subfolder/file.mkv"));

        // Subpath with trailing slash
        Assert.That(serviceWithTrailing.Remap("seedbox", "/remote/downloads/subfolder/"),
            Is.EqualTo(serviceWithoutTrailing.Remap("seedbox", "/remote/downloads/subfolder/")));
        Assert.That(serviceWithTrailing.Remap("seedbox", "/remote/downloads/subfolder/"),
            Is.EqualTo("/local/downloads/subfolder/"));

        // Exact match without trailing slash
        Assert.That(serviceWithTrailing.Remap("seedbox", "/remote/downloads"),
            Is.EqualTo(serviceWithoutTrailing.Remap("seedbox", "/remote/downloads")));
        Assert.That(serviceWithTrailing.Remap("seedbox", "/remote/downloads"),
            Is.EqualTo("/local/downloads"));

        // Exact match with trailing slash
        Assert.That(serviceWithTrailing.Remap("seedbox", "/remote/downloads/"),
            Is.EqualTo(serviceWithoutTrailing.Remap("seedbox", "/remote/downloads/")));
        Assert.That(serviceWithTrailing.Remap("seedbox", "/remote/downloads/"),
            Is.EqualTo("/local/downloads/"));
    }

    [Test]
    public void Remap_respects_cross_platform_case_sensitivity()
    {
        // POSIX path input: case sensitive
        _service.Add(new RemotePathMapping
        {
            Host = "posix-host",
            RemotePath = "/media/downloads",
            LocalPath = "/local/downloads"
        });

        var posixMatch = _service.Remap("posix-host", "/media/downloads/file.mkv");
        Assert.That(posixMatch, Is.EqualTo("/local/downloads/file.mkv"));

        var posixMismatch = _service.Remap("posix-host", "/Media/Downloads/file.mkv");
        Assert.That(posixMismatch, Is.EqualTo("/Media/Downloads/file.mkv"));

        // Windows path input: case insensitive
        _service.Add(new RemotePathMapping
        {
            Host = "win-host",
            RemotePath = @"D:\Media\Downloads",
            LocalPath = "/local/downloads"
        });

        var winMatch = _service.Remap("win-host", @"d:\media\downloads\file.mkv");
        Assert.That(winMatch, Is.EqualTo("/local/downloads/file.mkv"));
    }

    [Test]
    public void Remap_matches_exact_ip_and_strips_port()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "192.168.1.50",
            RemotePath = "/remote/downloads",
            LocalPath = "/local/downloads"
        });

        var resultWithPort = _service.Remap("192.168.1.50:8080", "/remote/downloads/file.mkv");
        Assert.That(resultWithPort, Is.EqualTo("/local/downloads/file.mkv"));

        var resultWithoutPort = _service.Remap("192.168.1.50", "/remote/downloads/file.mkv");
        Assert.That(resultWithoutPort, Is.EqualTo("/local/downloads/file.mkv"));
    }

    [Test]
    public void Remap_matches_cidr_subnet()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "192.168.1.0/24",
            RemotePath = "/remote/downloads",
            LocalPath = "/local/downloads"
        });

        var inSubnet = _service.Remap("192.168.1.105", "/remote/downloads/file.mkv");
        Assert.That(inSubnet, Is.EqualTo("/local/downloads/file.mkv"));

        var outOfSubnet = _service.Remap("192.168.2.105", "/remote/downloads/file.mkv");
        Assert.That(outOfSubnet, Is.EqualTo("/remote/downloads/file.mkv"));
    }

    [Test]
    public void Remap_matches_wildcard_fallback()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "*",
            RemotePath = "/remote/downloads",
            LocalPath = "/local/downloads"
        });

        var result = _service.Remap("unmapped-client.lan", "/remote/downloads/file.mkv");
        Assert.That(result, Is.EqualTo("/local/downloads/file.mkv"));
    }

    [Test]
    public void Remap_matches_default_and_all_as_wildcard_fallback()
    {
        var defaultService = new RemotePathMappingService();
        defaultService.Add(new RemotePathMapping
        {
            Host = "default",
            RemotePath = "/remote/downloads",
            LocalPath = "/local/downloads"
        });

        Assert.That(defaultService.Remap("random-caller", "/remote/downloads/file.mkv"), Is.EqualTo("/local/downloads/file.mkv"));

        var allService = new RemotePathMappingService();
        allService.Add(new RemotePathMapping
        {
            Host = "all",
            RemotePath = "/remote/downloads",
            LocalPath = "/local/downloads"
        });

        Assert.That(allService.Remap("random-caller", "/remote/downloads/file.mkv"), Is.EqualTo("/local/downloads/file.mkv"));
    }

    [Test]
    public void Precedence_exact_takes_priority_over_cidr_and_wildcard()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "*",
            RemotePath = "/remote/downloads",
            LocalPath = "/wildcard/downloads"
        });

        _service.Add(new RemotePathMapping
        {
            Host = "192.168.1.0/24",
            RemotePath = "/remote/downloads",
            LocalPath = "/cidr/downloads"
        });

        _service.Add(new RemotePathMapping
        {
            Host = "192.168.1.105",
            RemotePath = "/remote/downloads",
            LocalPath = "/exact/downloads"
        });

        // Exact match
        var exactResult = _service.Remap("192.168.1.105", "/remote/downloads/file.mkv");
        Assert.That(exactResult, Is.EqualTo("/exact/downloads/file.mkv"));

        // CIDR match (different IP in same subnet)
        var cidrResult = _service.Remap("192.168.1.50", "/remote/downloads/file.mkv");
        Assert.That(cidrResult, Is.EqualTo("/cidr/downloads/file.mkv"));

        // Wildcard match (outside subnet)
        var wildcardResult = _service.Remap("10.0.0.1", "/remote/downloads/file.mkv");
        Assert.That(wildcardResult, Is.EqualTo("/wildcard/downloads/file.mkv"));
    }

    [Test]
    public void Precedence_more_specific_cidr_takes_priority_over_broad_cidr()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "192.168.0.0/16",
            RemotePath = "/remote/downloads",
            LocalPath = "/broad/downloads"
        });

        _service.Add(new RemotePathMapping
        {
            Host = "192.168.1.0/24",
            RemotePath = "/remote/downloads",
            LocalPath = "/specific/downloads"
        });

        var result = _service.Remap("192.168.1.105", "/remote/downloads/file.mkv");
        Assert.That(result, Is.EqualTo("/specific/downloads/file.mkv"));

        var broadResult = _service.Remap("192.168.2.105", "/remote/downloads/file.mkv");
        Assert.That(broadResult, Is.EqualTo("/broad/downloads/file.mkv"));
    }

    [Test]
    public void Remap_falls_back_to_wildcard_when_exact_rule_prefix_does_not_match()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "sonarr.lan",
            RemotePath = "/remote/tv",
            LocalPath = "/local/tv"
        });

        _service.Add(new RemotePathMapping
        {
            Host = "*",
            RemotePath = "/remote/movies",
            LocalPath = "/local/movies"
        });

        var result = _service.Remap("sonarr.lan", "/remote/movies/avatar.mkv");
        Assert.That(result, Is.EqualTo("/local/movies/avatar.mkv"));
    }

    [Test]
    public void RemapLocalToRemote_translates_local_path_to_remote_path()
    {
        _service.Add(new RemotePathMapping
        {
            Host = "192.168.1.0/24",
            RemotePath = @"D:\Downloads",
            LocalPath = "/local/downloads"
        });

        var remotePath = _service.RemapLocalToRemote("192.168.1.55", "/local/downloads/movie/file.mkv");
        Assert.That(remotePath, Is.EqualTo(@"D:\Downloads\movie\file.mkv"));
    }

    [Test]
    public void CallerHostResolver_resolves_headers_in_priority_order()
    {
        var resolver = new CallerHostResolver();

        // 1. X-Forwarded-For first entry
        var fromFwdFor = resolver.ResolveFromHeaders("203.0.113.195, 70.41.3.18", "198.51.100.1", "proxy.lan", "10.0.0.1");
        Assert.That(fromFwdFor, Is.EqualTo("203.0.113.195"));

        // 2. X-Forwarded-For with port
        var fromFwdForPort = resolver.ResolveFromHeaders("203.0.113.195:8080", null, null, null);
        Assert.That(fromFwdForPort, Is.EqualTo("203.0.113.195"));

        // 3. X-Real-IP when X-Forwarded-For is empty
        var fromRealIp = resolver.ResolveFromHeaders(null, "198.51.100.1", "proxy.lan", "10.0.0.1");
        Assert.That(fromRealIp, Is.EqualTo("198.51.100.1"));

        // 4. X-Forwarded-Host when X-Forwarded-For and X-Real-IP are empty
        var fromFwdHost = resolver.ResolveFromHeaders(null, null, "client.lan:8989", "10.0.0.1");
        Assert.That(fromFwdHost, Is.EqualTo("client.lan"));

        // 5. Remote socket IP when headers are empty
        var fromRemoteIp = resolver.ResolveFromHeaders(null, null, null, "10.0.0.1:54321");
        Assert.That(fromRemoteIp, Is.EqualTo("10.0.0.1"));

        // 6. Default fallback
        var fromDefault = resolver.ResolveFromHeaders(null, null, null, null);
        Assert.That(fromDefault, Is.EqualTo("localhost"));
    }
}
