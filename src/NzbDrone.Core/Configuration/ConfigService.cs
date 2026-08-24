using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration;

public interface IConfigService
{
    void SaveConfigDictionary(Dictionary<string, object> configValues);
    bool GetValueBoolean(string key, bool defaultValue = false);
    string GetValue(string key, string defaultValue = "");
    int GetValueInt(string key, int defaultValue = 0);
    double GetValueDouble(string key, double defaultValue = 0.0);

    // General
    bool AutoStart { get; }
    string ThemeStyle { get; }
    string ColorScheme { get; }

    // Watch Folder
    bool WatchFolderEnabled { get; }
    string WatchFolderPath { get; }
    int WatchFolderScanIntervalSeconds { get; }
    bool WatchFolderAutoStartTorrents { get; }
    bool WatchFolderDeleteAddedTorrents { get; }

    // Connection
    int ListeningPort { get; }
    bool UpnpEnabled { get; }
    int MaxGlobalConnections { get; }
    int MaxPerTorrentConnections { get; }
    int MaxUploadSlots { get; }

    // Proxy
    string ProxyType { get; }
    string ProxyHost { get; }
    int ProxyPort { get; }
    bool ProxyAuthEnabled { get; }
    string ProxyUsername { get; }
    string ProxyPassword { get; }

    // BitTorrent
    bool EnableDht { get; }
    bool EnablePex { get; }
    bool EnableLpd { get; }
    string EncryptionMode { get; }
    string BitTorrentUserAgent { get; }
    string PeerIdPrefix { get; }
    int AnnounceIntervalSeconds { get; }
    int MinAnnounceIntervalSeconds { get; }
    int ScrapeIntervalSeconds { get; }

    // Speed
    int MaxUploadSpeedKbps { get; }
    int MaxDownloadSpeedKbps { get; }
    bool AlternativeSpeedEnabled { get; }
    int AltUploadSpeedKbps { get; }
    int AltDownloadSpeedKbps { get; }
    double GlobalSeedRatioLimit { get; }

    // Speed Distribution
    string UploadDistributionAlgorithm { get; }
    int UploadDistributionSpreadPercentage { get; }
    string UploadRedistributionMode { get; }
    int UploadCustomIntervalMinutes { get; }
    int UploadStoppedMinPercentage { get; }
    int UploadStoppedMaxPercentage { get; }
    string DownloadDistributionAlgorithm { get; }
    int DownloadDistributionSpreadPercentage { get; }
    string DownloadRedistributionMode { get; }
    int DownloadCustomIntervalMinutes { get; }
    int DownloadStoppedMinPercentage { get; }
    int DownloadStoppedMaxPercentage { get; }
    double SpeedVariationMin { get; }
    double SpeedVariationMax { get; }
    int DownloadThresholdPercent { get; }

    // Scheduler
    bool SchedulerEnabled { get; }
    int SchedulerStartHour { get; }
    int SchedulerStartMinute { get; }
    int SchedulerEndHour { get; }
    int SchedulerEndMinute { get; }
    bool SchedulerMonday { get; }
    bool SchedulerTuesday { get; }
    bool SchedulerWednesday { get; }
    bool SchedulerThursday { get; }
    bool SchedulerFriday { get; }
    bool SchedulerSaturday { get; }
    bool SchedulerSunday { get; }

    // Peer Protocol
    int HandshakeTimeoutSeconds { get; }
    int MessageReadTimeoutSeconds { get; }
    int KeepAliveIntervalSeconds { get; }
    int PeerContactIntervalSeconds { get; }
    int UdpTrackerTimeoutSeconds { get; }
    int HttpTrackerTimeoutSeconds { get; }
    int PeerRequestCount { get; }

    // Peer Behavior
    double SeederUploadActivityProbability { get; }
    double PeerIdleChance { get; }
    double PeerDropoutProbability { get; }
    double ConnectionRotationPercentage { get; }

    // Protocol Extensions
    bool ExtensionUtMetadata { get; }
    bool ExtensionUtPex { get; }
    bool ExtensionLtDontHave { get; }
    bool ExtensionFastExtension { get; }
    bool UtpEnabled { get; }
    bool TcpFallback { get; }
    int TransportConnectionTimeoutSeconds { get; }
    int PexInterval { get; }
    int PexMaxPeersPerMessage { get; }

    // Multi-Tracker
    bool MultiTrackerEnabled { get; }
    bool MultiTrackerFailoverEnabled { get; }
    bool AnnounceToAllTiers { get; }
    bool AnnounceToAllInTier { get; }
    int FailoverMaxConsecutiveFailures { get; }
    int FailoverBackoffBaseSeconds { get; }
    int FailoverMaxBackoffSeconds { get; }

    // DHT
    int DhtRoutingTableSize { get; }
    int DhtAnnouncementInterval { get; }
    int DhtBootstrapTimeout { get; }
    int DhtQueryTimeout { get; }
    int DhtMaxNodes { get; }
    int DhtBucketSize { get; }
    int DhtConcurrentQueries { get; }
    bool DhtAutoBootstrap { get; }
    bool DhtRateLimitEnabled { get; }
    int DhtMaxQueriesPerSecond { get; }

    // Tracker Server
    bool TrackerServerEnabled { get; }
    bool TrackerHttpEnabled { get; }
    int TrackerHttpPort { get; }
    bool TrackerUdpEnabled { get; }
    int TrackerUdpPort { get; }
    string TrackerBindAddress { get; }
    int TrackerAnnounceInterval { get; }
    int TrackerMaxPeersPerAnnounce { get; }
    bool TrackerEnableScrape { get; }
    bool TrackerPrivateMode { get; }
    bool TrackerLogAnnounces { get; }
    int TrackerRateLimitPerMinute { get; }

    // Advanced/Logging
    bool LogToFile { get; }
    string FileLogLevel { get; }
    bool DebugMode { get; }
    int UiRefreshRateSec { get; }
}

public class ConfigModel : ModelBase
{
    public string Key { get; set; }
    public string Value { get; set; }
}

public class ConfigService : IConfigService
{
    private readonly IBasicRepository<ConfigModel> _repository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly object _cacheLock = new();
    private volatile Dictionary<string, string> _cache;

    public ConfigService(IBasicRepository<ConfigModel> repository, IEventAggregator eventAggregator)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void SaveConfigDictionary(Dictionary<string, object> configValues)
    {
        lock (_cacheLock)
        {
            var all = _repository.All().ToList();

            foreach (var configValue in configValues)
            {
                var existing = all.FirstOrDefault(c =>
                    string.Equals(c.Key, configValue.Key, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    _repository.Insert(new ConfigModel { Key = configValue.Key, Value = configValue.Value?.ToString() ?? string.Empty });
                }
                else
                {
                    existing.Value = configValue.Value?.ToString() ?? string.Empty;
                    _repository.Update(existing);
                }
            }

            _cache = null;
        }

        _logger.Debug("Saved {0} config values", configValues.Count);
        _eventAggregator.PublishEvent(new ConfigSavedEvent());
    }

    public bool GetValueBoolean(string key, bool defaultValue = false)
    {
        var value = GetValue(key, string.Empty);

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    public string GetValue(string key, string defaultValue = "")
    {
        var snapshot = _cache;

        if (snapshot == null)
        {
            lock (_cacheLock)
            {
                snapshot = _cache;

                if (snapshot == null)
                {
                    snapshot = _repository.All()
                        .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
                    _cache = snapshot;
                }
            }
        }

        return snapshot.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public int GetValueInt(string key, int defaultValue = 0)
    {
        var value = GetValue(key, string.Empty);

        if (int.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    public double GetValueDouble(string key, double defaultValue = 0.0)
    {
        var value = GetValue(key, string.Empty);

        if (double.TryParse(value, out var result))
        {
            return result;
        }

        return defaultValue;
    }

    // Speed limits are always finite: a stored value of 0 (legacy "unlimited") resolves to the default.
    private int GetPositiveSpeedKbps(string key, int defaultValue)
    {
        var value = GetValueInt(key, defaultValue);
        return value > 0 ? value : defaultValue;
    }

    // General
    public bool AutoStart => GetValueBoolean("AutoStart", true);
    public string ThemeStyle => GetValue("ThemeStyle", "system");
    public string ColorScheme => GetValue("ColorScheme", "auto");

    // Watch Folder
    public bool WatchFolderEnabled => GetValueBoolean("WatchFolderEnabled", false);
    public string WatchFolderPath => GetValue("WatchFolderPath", "");
    public int WatchFolderScanIntervalSeconds => GetValueInt("WatchFolderScanIntervalSeconds", 10);
    public bool WatchFolderAutoStartTorrents => GetValueBoolean("WatchFolderAutoStartTorrents", true);
    public bool WatchFolderDeleteAddedTorrents => GetValueBoolean("WatchFolderDeleteAddedTorrents", false);

    // Connection
    public int ListeningPort => GetValueInt("ListeningPort", 6881);
    public bool UpnpEnabled => GetValueBoolean("UpnpEnabled", true);
    public int MaxGlobalConnections => GetValueInt("MaxGlobalConnections", 200);
    public int MaxPerTorrentConnections => GetValueInt("MaxPerTorrentConnections", 50);
    public int MaxUploadSlots => GetValueInt("MaxUploadSlots", 4);

    // Proxy
    public string ProxyType => GetValue("ProxyType", "none");
    public string ProxyHost => GetValue("ProxyHost", "");
    public int ProxyPort => GetValueInt("ProxyPort", 8080);
    public bool ProxyAuthEnabled => GetValueBoolean("ProxyAuthEnabled", false);
    public string ProxyUsername => GetValue("ProxyUsername", "");
    public string ProxyPassword => GetValue("ProxyPassword", "");

    // BitTorrent
    public bool EnableDht => GetValueBoolean("EnableDht", true);
    public bool EnablePex => GetValueBoolean("EnablePex", true);
    public bool EnableLpd => GetValueBoolean("EnableLpd", true);
    public string EncryptionMode => GetValue("EncryptionMode", "enabled");
    public string BitTorrentUserAgent => GetValue("BitTorrentUserAgent", "qBittorrent/4.4.2");
    public string PeerIdPrefix => GetValue("PeerIdPrefix", "-qB4420-");
    public int AnnounceIntervalSeconds => GetValueInt("AnnounceIntervalSeconds", 1800);
    public int MinAnnounceIntervalSeconds => GetValueInt("MinAnnounceIntervalSeconds", 300);
    public int ScrapeIntervalSeconds => GetValueInt("ScrapeIntervalSeconds", 900);

    // Speed
    public const int DefaultMaxUploadSpeedKbps = 625;
    public const int DefaultMaxDownloadSpeedKbps = 1250;

    public int MaxUploadSpeedKbps => GetPositiveSpeedKbps("MaxUploadSpeedKbps", DefaultMaxUploadSpeedKbps);
    public int MaxDownloadSpeedKbps => GetPositiveSpeedKbps("MaxDownloadSpeedKbps", DefaultMaxDownloadSpeedKbps);
    public bool AlternativeSpeedEnabled => GetValueBoolean("AlternativeSpeedEnabled", false);
    public int AltUploadSpeedKbps => GetPositiveSpeedKbps("AltUploadSpeedKbps", 50);
    public int AltDownloadSpeedKbps => GetPositiveSpeedKbps("AltDownloadSpeedKbps", 100);
    public double GlobalSeedRatioLimit => GetValueDouble("GlobalSeedRatioLimit", 0.0);

    // Speed Distribution
    public string UploadDistributionAlgorithm => GetValue("UploadDistributionAlgorithm", "Equal");
    public int UploadDistributionSpreadPercentage => GetValueInt("UploadDistributionSpreadPercentage", 50);
    public string UploadRedistributionMode => GetValue("UploadRedistributionMode", "tick");
    public int UploadCustomIntervalMinutes => GetValueInt("UploadCustomIntervalMinutes", 5);
    public int UploadStoppedMinPercentage => GetValueInt("UploadStoppedMinPercentage", 20);
    public int UploadStoppedMaxPercentage => GetValueInt("UploadStoppedMaxPercentage", 40);
    public string DownloadDistributionAlgorithm => GetValue("DownloadDistributionAlgorithm", "Equal");
    public int DownloadDistributionSpreadPercentage => GetValueInt("DownloadDistributionSpreadPercentage", 50);
    public string DownloadRedistributionMode => GetValue("DownloadRedistributionMode", "tick");
    public int DownloadCustomIntervalMinutes => GetValueInt("DownloadCustomIntervalMinutes", 5);
    public int DownloadStoppedMinPercentage => GetValueInt("DownloadStoppedMinPercentage", 20);
    public int DownloadStoppedMaxPercentage => GetValueInt("DownloadStoppedMaxPercentage", 40);
    public double SpeedVariationMin => GetValueDouble("SpeedVariationMin", 0.2);
    public double SpeedVariationMax => GetValueDouble("SpeedVariationMax", 0.8);
    public int DownloadThresholdPercent => GetValueInt("DownloadThresholdPercent", 30);

    // Scheduler
    public bool SchedulerEnabled => GetValueBoolean("SchedulerEnabled", false);
    public int SchedulerStartHour => GetValueInt("SchedulerStartHour", 22);
    public int SchedulerStartMinute => GetValueInt("SchedulerStartMinute", 0);
    public int SchedulerEndHour => GetValueInt("SchedulerEndHour", 6);
    public int SchedulerEndMinute => GetValueInt("SchedulerEndMinute", 0);
    public bool SchedulerMonday => GetValueBoolean("SchedulerMonday", true);
    public bool SchedulerTuesday => GetValueBoolean("SchedulerTuesday", true);
    public bool SchedulerWednesday => GetValueBoolean("SchedulerWednesday", true);
    public bool SchedulerThursday => GetValueBoolean("SchedulerThursday", true);
    public bool SchedulerFriday => GetValueBoolean("SchedulerFriday", true);
    public bool SchedulerSaturday => GetValueBoolean("SchedulerSaturday", true);
    public bool SchedulerSunday => GetValueBoolean("SchedulerSunday", true);

    // Peer Protocol
    public int HandshakeTimeoutSeconds => GetValueInt("HandshakeTimeoutSeconds", 30);
    public int MessageReadTimeoutSeconds => GetValueInt("MessageReadTimeoutSeconds", 60);
    public int KeepAliveIntervalSeconds => GetValueInt("KeepAliveIntervalSeconds", 120);
    public int PeerContactIntervalSeconds => GetValueInt("PeerContactIntervalSeconds", 300);
    public int UdpTrackerTimeoutSeconds => GetValueInt("UdpTrackerTimeoutSeconds", 5);
    public int HttpTrackerTimeoutSeconds => GetValueInt("HttpTrackerTimeoutSeconds", 10);
    public int PeerRequestCount => GetValueInt("PeerRequestCount", 200);

    // Peer Behavior
    public double SeederUploadActivityProbability => GetValueDouble("SeederUploadActivityProbability", 0.85);
    public double PeerIdleChance => GetValueDouble("PeerIdleChance", 0.3);
    public double PeerDropoutProbability => GetValueDouble("PeerDropoutProbability", 0.1);
    public double ConnectionRotationPercentage => GetValueDouble("ConnectionRotationPercentage", 0.25);

    // Protocol Extensions
    public bool ExtensionUtMetadata => GetValueBoolean("ExtensionUtMetadata", true);
    public bool ExtensionUtPex => GetValueBoolean("ExtensionUtPex", true);
    public bool ExtensionLtDontHave => GetValueBoolean("ExtensionLtDontHave", true);
    public bool ExtensionFastExtension => GetValueBoolean("ExtensionFastExtension", true);
    public bool UtpEnabled => GetValueBoolean("UtpEnabled", false);
    public bool TcpFallback => GetValueBoolean("TcpFallback", true);
    public int TransportConnectionTimeoutSeconds => GetValueInt("TransportConnectionTimeoutSeconds", 30);
    public int PexInterval => GetValueInt("PexInterval", 60);
    public int PexMaxPeersPerMessage => GetValueInt("PexMaxPeersPerMessage", 50);

    // Multi-Tracker
    public bool MultiTrackerEnabled => GetValueBoolean("MultiTrackerEnabled", true);
    public bool MultiTrackerFailoverEnabled => GetValueBoolean("MultiTrackerFailoverEnabled", true);
    public bool AnnounceToAllTiers => GetValueBoolean("AnnounceToAllTiers", false);
    public bool AnnounceToAllInTier => GetValueBoolean("AnnounceToAllInTier", false);
    public int FailoverMaxConsecutiveFailures => GetValueInt("FailoverMaxConsecutiveFailures", 5);
    public int FailoverBackoffBaseSeconds => GetValueInt("FailoverBackoffBaseSeconds", 60);
    public int FailoverMaxBackoffSeconds => GetValueInt("FailoverMaxBackoffSeconds", 3600);

    // DHT
    public int DhtRoutingTableSize => GetValueInt("DhtRoutingTableSize", 160);
    public int DhtAnnouncementInterval => GetValueInt("DhtAnnouncementInterval", 1800);
    public int DhtBootstrapTimeout => GetValueInt("DhtBootstrapTimeout", 30);
    public int DhtQueryTimeout => GetValueInt("DhtQueryTimeout", 10);
    public int DhtMaxNodes => GetValueInt("DhtMaxNodes", 1000);
    public int DhtBucketSize => GetValueInt("DhtBucketSize", 8);
    public int DhtConcurrentQueries => GetValueInt("DhtConcurrentQueries", 3);
    public bool DhtAutoBootstrap => GetValueBoolean("DhtAutoBootstrap", true);
    public bool DhtRateLimitEnabled => GetValueBoolean("DhtRateLimitEnabled", true);
    public int DhtMaxQueriesPerSecond => GetValueInt("DhtMaxQueriesPerSecond", 10);

    // Tracker Server
    public bool TrackerServerEnabled => GetValueBoolean("TrackerServerEnabled", false);
    public bool TrackerHttpEnabled => GetValueBoolean("TrackerHttpEnabled", true);
    public int TrackerHttpPort => GetValueInt("TrackerHttpPort", 9696);
    public bool TrackerUdpEnabled => GetValueBoolean("TrackerUdpEnabled", true);
    public int TrackerUdpPort => GetValueInt("TrackerUdpPort", 6969);
    public string TrackerBindAddress => GetValue("TrackerBindAddress", "0.0.0.0");
    public int TrackerAnnounceInterval => GetValueInt("TrackerAnnounceInterval", 1800);
    public int TrackerMaxPeersPerAnnounce => GetValueInt("TrackerMaxPeersPerAnnounce", 50);
    public bool TrackerEnableScrape => GetValueBoolean("TrackerEnableScrape", true);
    public bool TrackerPrivateMode => GetValueBoolean("TrackerPrivateMode", false);
    public bool TrackerLogAnnounces => GetValueBoolean("TrackerLogAnnounces", false);
    public int TrackerRateLimitPerMinute => GetValueInt("TrackerRateLimitPerMinute", 60);

    // Advanced/Logging
    public bool LogToFile => GetValueBoolean("LogToFile", true);
    public string FileLogLevel => GetValue("FileLogLevel", "Info");
    public bool DebugMode => GetValueBoolean("DebugMode", false);
    public int UiRefreshRateSec => GetValueInt("UiRefreshRateSec", 9);
}

public class ConfigSavedEvent : IEvent
{
}
