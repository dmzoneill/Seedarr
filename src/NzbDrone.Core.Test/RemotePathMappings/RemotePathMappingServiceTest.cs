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
}
