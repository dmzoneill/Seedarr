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

    public ConfigService(IBasicRepository<ConfigModel> repository, IEventAggregator eventAggregator)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void SaveConfigDictionary(Dictionary<string, object> configValues)
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
        var all = _repository.All().ToList();
        var config = all.FirstOrDefault(c =>
            string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

        return config?.Value ?? defaultValue;
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
}

public class ConfigSavedEvent : IEvent
{
}
