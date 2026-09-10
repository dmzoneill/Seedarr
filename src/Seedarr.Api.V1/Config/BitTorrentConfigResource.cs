using NzbDrone.Core.Configuration;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Config;

public class BitTorrentConfigResource : RestResource
{
    public bool EnableDht { get; set; }
    public bool EnablePex { get; set; }
    public bool EnableLpd { get; set; }
    public string EncryptionMode { get; set; }
    public string BitTorrentUserAgent { get; set; }
    public string PeerIdPrefix { get; set; }
    public int AnnounceIntervalSeconds { get; set; }
    public int MinAnnounceIntervalSeconds { get; set; }
    public int ScrapeIntervalSeconds { get; set; }

    // Lifecycle Scripts
    public string OnDownloadCompleteScript { get; set; }
    public string OnSeedGoalReachedScript { get; set; }
    public string ScriptTorrentDoneFilename { get; set; }
    public string ScriptTorrentAddedFilename { get; set; }
    public string ScriptTorrentDoneSeedingFilename { get; set; }
    public int CustomScriptTimeoutSeconds { get; set; }
}

public static class BitTorrentConfigResourceMapper
{
    public static BitTorrentConfigResource ToResource(IConfigService model)
    {
        return new BitTorrentConfigResource
        {
            EnableDht = model.EnableDht,
            EnablePex = model.EnablePex,
            EnableLpd = model.EnableLpd,
            EncryptionMode = model.EncryptionMode,
            BitTorrentUserAgent = model.BitTorrentUserAgent,
            PeerIdPrefix = model.PeerIdPrefix,
            AnnounceIntervalSeconds = model.AnnounceIntervalSeconds,
            MinAnnounceIntervalSeconds = model.MinAnnounceIntervalSeconds,
            ScrapeIntervalSeconds = model.ScrapeIntervalSeconds,
            OnDownloadCompleteScript = model.OnDownloadCompleteScript,
            OnSeedGoalReachedScript = model.OnSeedGoalReachedScript,
            ScriptTorrentDoneFilename = model.ScriptTorrentDoneFilename,
            ScriptTorrentAddedFilename = model.ScriptTorrentAddedFilename,
            ScriptTorrentDoneSeedingFilename = model.ScriptTorrentDoneSeedingFilename,
            CustomScriptTimeoutSeconds = model.CustomScriptTimeoutSeconds
        };
    }
}
