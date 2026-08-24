using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class ConfigServiceTest
    {
        private IBasicRepository<ConfigModel> _repository;
        private IEventAggregator _eventAggregator;
        private ConfigService _subject;

        [SetUp]
        public void Setup()
        {
            _repository = Substitute.For<IBasicRepository<ConfigModel>>();
            _eventAggregator = Substitute.For<IEventAggregator>();
            _subject = new ConfigService(_repository, _eventAggregator);
        }

        // ---- GetValue tests ----

        [Test]
        public void GetValue_should_return_value_when_key_exists_in_repo()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "TestKey", Value = "TestValue" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValue("TestKey");

            Assert.That(result, Is.EqualTo("TestValue"));
        }

        [Test]
        public void GetValue_should_return_default_when_key_not_found()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var result = _subject.GetValue("NonExistentKey", "DefaultValue");

            Assert.That(result, Is.EqualTo("DefaultValue"));
        }

        [Test]
        public void GetValue_should_perform_case_insensitive_key_lookup()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "TestKey", Value = "TestValue" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValue("testkey");

            Assert.That(result, Is.EqualTo("TestValue"));
        }

        [Test]
        public void GetValue_should_cache_after_first_call()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "TestKey", Value = "TestValue" }
            };
            _repository.All().Returns(configs.AsQueryable());

            _subject.GetValue("TestKey");
            _subject.GetValue("TestKey");

            _repository.Received(1).All();
        }

        [Test]
        public void GetValue_should_use_cache_on_second_call_and_not_call_repo_again()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "CachedKey", Value = "CachedValue" }
            };
            _repository.All().Returns(configs.AsQueryable());

            _subject.GetValue("CachedKey");
            _subject.GetValue("AnotherKey", "DefaultValue");

            _repository.Received(1).All();
        }

        [Test]
        public void GetValue_should_return_empty_string_as_default_when_no_default_specified()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var result = _subject.GetValue("MissingKey");

            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void GetValue_should_return_stored_empty_string()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "EmptyKey", Value = "" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValue("EmptyKey", "SomeDefault");

            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void GetValue_should_handle_upper_case_key_lookup()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "lowerkey", Value = "val" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValue("LOWERKEY");

            Assert.That(result, Is.EqualTo("val"));
        }

        // ---- GetValueBoolean tests ----

        [Test]
        public void GetValueBoolean_should_return_true_when_value_is_true()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "BoolKey", Value = "True" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueBoolean("BoolKey");

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetValueBoolean_should_return_false_when_value_is_false()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "BoolKey", Value = "False" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueBoolean("BoolKey");

            Assert.That(result, Is.False);
        }

        [Test]
        public void GetValueBoolean_should_return_default_when_value_is_not_parseable()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "BoolKey", Value = "NotABool" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueBoolean("BoolKey", true);

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetValueBoolean_should_return_default_when_key_not_found()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var result = _subject.GetValueBoolean("NonExistentKey", true);

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetValueBoolean_should_return_false_default_when_not_specified()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var result = _subject.GetValueBoolean("Missing");

            Assert.That(result, Is.False);
        }

        [Test]
        public void GetValueBoolean_should_handle_case_insensitive_true()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "BoolKey", Value = "true" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueBoolean("BoolKey");

            Assert.That(result, Is.True);
        }

        [Test]
        public void GetValueBoolean_should_return_default_for_empty_string()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "BoolKey", Value = "" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueBoolean("BoolKey", true);

            // Empty string -> GetValue returns empty -> bool.TryParse("") returns false -> defaultValue
            Assert.That(result, Is.True);
        }

        // ---- GetValueInt tests ----

        [Test]
        public void GetValueInt_should_return_int_when_value_is_valid_int()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "IntKey", Value = "42" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueInt("IntKey");

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetValueInt_should_return_default_when_value_is_not_parseable()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "IntKey", Value = "NotAnInt" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueInt("IntKey", 100);

            Assert.That(result, Is.EqualTo(100));
        }

        [Test]
        public void GetValueInt_should_return_default_when_key_not_found()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var result = _subject.GetValueInt("NonExistentKey", 100);

            Assert.That(result, Is.EqualTo(100));
        }

        [Test]
        public void GetValueInt_should_return_zero_default_when_not_specified()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var result = _subject.GetValueInt("Missing");

            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void GetValueInt_should_parse_negative_int()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "IntKey", Value = "-5" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueInt("IntKey");

            Assert.That(result, Is.EqualTo(-5));
        }

        [Test]
        public void GetValueInt_should_return_default_for_double_value()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "IntKey", Value = "3.14" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueInt("IntKey", 99);

            Assert.That(result, Is.EqualTo(99));
        }

        // ---- GetValueDouble tests ----

        [Test]
        public void GetValueDouble_should_return_double_when_value_is_valid_double()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "DoubleKey", Value = "3.14" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueDouble("DoubleKey");

            Assert.That(result, Is.EqualTo(3.14));
        }

        [Test]
        public void GetValueDouble_should_return_default_when_value_is_not_parseable()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "DoubleKey", Value = "NotADouble" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueDouble("DoubleKey", 1.5);

            Assert.That(result, Is.EqualTo(1.5));
        }

        [Test]
        public void GetValueDouble_should_return_default_when_key_not_found()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var result = _subject.GetValueDouble("NonExistentKey", 1.5);

            Assert.That(result, Is.EqualTo(1.5));
        }

        [Test]
        public void GetValueDouble_should_return_zero_default_when_not_specified()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var result = _subject.GetValueDouble("Missing");

            Assert.That(result, Is.EqualTo(0.0));
        }

        [Test]
        public void GetValueDouble_should_parse_negative_double()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "DoubleKey", Value = "-2.5" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueDouble("DoubleKey");

            Assert.That(result, Is.EqualTo(-2.5));
        }

        [Test]
        public void GetValueDouble_should_parse_integer_as_double()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "DoubleKey", Value = "42" }
            };
            _repository.All().Returns(configs.AsQueryable());

            var result = _subject.GetValueDouble("DoubleKey");

            Assert.That(result, Is.EqualTo(42.0));
        }

        // ---- SaveConfigDictionary tests ----

        [Test]
        public void SaveConfigDictionary_should_insert_new_config_when_key_does_not_exist()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "NewKey", "NewValue" }
            };

            _subject.SaveConfigDictionary(configValues);

            _repository.Received(1).Insert(Arg.Is<ConfigModel>(c => c.Key == "NewKey" && c.Value == "NewValue"));
        }

        [Test]
        public void SaveConfigDictionary_should_update_existing_config_when_key_exists()
        {
            var existingConfig = new ConfigModel { Key = "ExistingKey", Value = "OldValue" };
            _repository.All().Returns(new List<ConfigModel> { existingConfig }.AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "existingkey", "NewValue" }
            };

            _subject.SaveConfigDictionary(configValues);

            _repository.Received(1).Update(Arg.Is<ConfigModel>(c => c.Key == "ExistingKey" && c.Value == "NewValue"));
        }

        [Test]
        public void SaveConfigDictionary_should_handle_null_value_by_converting_to_empty_string()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "NullKey", null }
            };

            _subject.SaveConfigDictionary(configValues);

            _repository.Received(1).Insert(Arg.Is<ConfigModel>(c => c.Key == "NullKey" && c.Value == string.Empty));
        }

        [Test]
        public void SaveConfigDictionary_should_invalidate_cache_after_save()
        {
            var initialConfigs = new List<ConfigModel>
            {
                new ConfigModel { Key = "TestKey", Value = "InitialValue" }
            };
            _repository.All().Returns(initialConfigs.AsQueryable());

            _subject.GetValue("TestKey");

            var updatedConfigs = new List<ConfigModel>
            {
                new ConfigModel { Key = "TestKey", Value = "UpdatedValue" }
            };
            _repository.All().Returns(updatedConfigs.AsQueryable());

            var configValues = new Dictionary<string, object>
            {
                { "TestKey", "UpdatedValue" }
            };
            _subject.SaveConfigDictionary(configValues);

            var result = _subject.GetValue("TestKey");

            Assert.That(result, Is.EqualTo("UpdatedValue"));
        }

        [Test]
        public void SaveConfigDictionary_should_publish_ConfigSavedEvent()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "Key", "Value" }
            };

            _subject.SaveConfigDictionary(configValues);

            _eventAggregator.Received(1).PublishEvent(Arg.Any<ConfigSavedEvent>());
        }

        [Test]
        public void SaveConfigDictionary_should_handle_multiple_values_in_one_call()
        {
            var existingConfig = new ConfigModel { Key = "ExistingKey", Value = "OldValue" };
            _repository.All().Returns(new List<ConfigModel> { existingConfig }.AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "ExistingKey", "UpdatedValue" },
                { "NewKey1", "Value1" },
                { "NewKey2", "Value2" }
            };

            _subject.SaveConfigDictionary(configValues);

            _repository.Received(1).Update(Arg.Is<ConfigModel>(c => c.Key == "ExistingKey" && c.Value == "UpdatedValue"));
            _repository.Received(1).Insert(Arg.Is<ConfigModel>(c => c.Key == "NewKey1" && c.Value == "Value1"));
            _repository.Received(1).Insert(Arg.Is<ConfigModel>(c => c.Key == "NewKey2" && c.Value == "Value2"));
        }

        [Test]
        public void SaveConfigDictionary_should_convert_int_value_to_string()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "IntKey", 42 }
            };

            _subject.SaveConfigDictionary(configValues);

            _repository.Received(1).Insert(Arg.Is<ConfigModel>(c => c.Key == "IntKey" && c.Value == "42"));
        }

        [Test]
        public void SaveConfigDictionary_should_convert_bool_value_to_string()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "BoolKey", true }
            };

            _subject.SaveConfigDictionary(configValues);

            _repository.Received(1).Insert(Arg.Is<ConfigModel>(c => c.Key == "BoolKey" && c.Value == "True"));
        }

        [Test]
        public void SaveConfigDictionary_should_convert_double_value_to_string()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "DoubleKey", 3.14 }
            };

            _subject.SaveConfigDictionary(configValues);

            _repository.Received(1).Insert(Arg.Is<ConfigModel>(c => c.Key == "DoubleKey" && c.Value == "3.14"));
        }

        [Test]
        public void SaveConfigDictionary_should_update_null_value_on_existing_key()
        {
            var existing = new ConfigModel { Key = "Key1", Value = "OldValue" };
            _repository.All().Returns(new List<ConfigModel> { existing }.AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "Key1", null }
            };

            _subject.SaveConfigDictionary(configValues);

            _repository.Received(1).Update(Arg.Is<ConfigModel>(c => c.Key == "Key1" && c.Value == string.Empty));
        }

        [Test]
        public void SaveConfigDictionary_should_call_repo_all_once_for_lookup()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());
            var configValues = new Dictionary<string, object>
            {
                { "A", "1" },
                { "B", "2" },
                { "C", "3" }
            };

            _subject.SaveConfigDictionary(configValues);

            // SaveConfigDictionary calls _repository.All() once at the start
            _repository.Received(1).All();
        }

        // ---- Cache invalidation ----

        [Test]
        public void Cache_should_be_rebuilt_after_SaveConfigDictionary()
        {
            // First: populate cache
            var initial = new List<ConfigModel>
            {
                new ConfigModel { Key = "A", Value = "1" }
            };
            _repository.All().Returns(initial.AsQueryable());
            _subject.GetValue("A"); // triggers cache build -> 1 call to All()

            // Save: invalidates cache, also calls All() once during save
            var updated = new List<ConfigModel>
            {
                new ConfigModel { Key = "A", Value = "2" }
            };

            // After save, _cache is set to null
            _subject.SaveConfigDictionary(new Dictionary<string, object> { { "A", "2" } });

            // Subsequent reads need fresh data
            _repository.All().Returns(updated.AsQueryable());
            var result = _subject.GetValue("A");

            Assert.That(result, Is.EqualTo("2"));
        }

        [Test]
        public void Multiple_gets_between_saves_should_use_cache()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "X", Value = "val" }
            };
            _repository.All().Returns(configs.AsQueryable());

            // Many reads should only hit repo once
            _subject.GetValue("X");
            _subject.GetValueBoolean("X");
            _subject.GetValueInt("X");
            _subject.GetValueDouble("X");

            _repository.Received(1).All();
        }

        // ---- Property defaults (General) ----

        [Test]
        public void AutoStart_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.AutoStart, Is.True);
        }

        [Test]
        public void ThemeStyle_should_default_to_system()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ThemeStyle, Is.EqualTo("system"));
        }

        [Test]
        public void ColorScheme_should_default_to_auto()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ColorScheme, Is.EqualTo("auto"));
        }

        // ---- Property defaults (Watch Folder) ----

        [Test]
        public void WatchFolderEnabled_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.WatchFolderEnabled, Is.False);
        }

        [Test]
        public void WatchFolderPath_should_default_to_empty()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.WatchFolderPath, Is.EqualTo(""));
        }

        [Test]
        public void WatchFolderScanIntervalSeconds_should_default_to_10()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.WatchFolderScanIntervalSeconds, Is.EqualTo(10));
        }

        [Test]
        public void WatchFolderAutoStartTorrents_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.WatchFolderAutoStartTorrents, Is.True);
        }

        [Test]
        public void WatchFolderDeleteAddedTorrents_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.WatchFolderDeleteAddedTorrents, Is.False);
        }

        // ---- Property defaults (Connection) ----

        [Test]
        public void ListeningPort_should_default_to_6881()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ListeningPort, Is.EqualTo(6881));
        }

        [Test]
        public void UpnpEnabled_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UpnpEnabled, Is.True);
        }

        [Test]
        public void MaxGlobalConnections_should_default_to_200()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MaxGlobalConnections, Is.EqualTo(200));
        }

        [Test]
        public void MaxPerTorrentConnections_should_default_to_50()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MaxPerTorrentConnections, Is.EqualTo(50));
        }

        [Test]
        public void MaxUploadSlots_should_default_to_4()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MaxUploadSlots, Is.EqualTo(4));
        }

        // ---- Property defaults (Proxy) ----

        [Test]
        public void ProxyType_should_default_to_none()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ProxyType, Is.EqualTo("none"));
        }

        [Test]
        public void ProxyHost_should_default_to_empty()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ProxyHost, Is.EqualTo(""));
        }

        [Test]
        public void ProxyPort_should_default_to_8080()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ProxyPort, Is.EqualTo(8080));
        }

        [Test]
        public void ProxyAuthEnabled_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ProxyAuthEnabled, Is.False);
        }

        [Test]
        public void ProxyUsername_should_default_to_empty()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ProxyUsername, Is.EqualTo(""));
        }

        [Test]
        public void ProxyPassword_should_default_to_empty()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ProxyPassword, Is.EqualTo(""));
        }

        // ---- Property defaults (BitTorrent) ----

        [Test]
        public void EnableDht_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.EnableDht, Is.True);
        }

        [Test]
        public void EnablePex_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.EnablePex, Is.True);
        }

        [Test]
        public void EnableLpd_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.EnableLpd, Is.True);
        }

        [Test]
        public void EncryptionMode_should_default_to_enabled()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.EncryptionMode, Is.EqualTo("enabled"));
        }

        [Test]
        public void BitTorrentUserAgent_should_default_to_qBittorrent()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.BitTorrentUserAgent, Is.EqualTo("qBittorrent/4.4.2"));
        }

        [Test]
        public void PeerIdPrefix_should_default_to_qB_prefix()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.PeerIdPrefix, Is.EqualTo("-qB4420-"));
        }

        [Test]
        public void AnnounceIntervalSeconds_should_default_to_1800()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.AnnounceIntervalSeconds, Is.EqualTo(1800));
        }

        [Test]
        public void MinAnnounceIntervalSeconds_should_default_to_300()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MinAnnounceIntervalSeconds, Is.EqualTo(300));
        }

        [Test]
        public void ScrapeIntervalSeconds_should_default_to_900()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ScrapeIntervalSeconds, Is.EqualTo(900));
        }

        // ---- Property defaults (Speed) ----

        [Test]
        public void MaxUploadSpeedKbps_should_default_to_625()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MaxUploadSpeedKbps, Is.EqualTo(625));
        }

        [Test]
        public void MaxDownloadSpeedKbps_should_default_to_1250()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MaxDownloadSpeedKbps, Is.EqualTo(1250));
        }

        [Test]
        public void MaxUploadSpeedKbps_should_resolve_stored_zero_to_default()
        {
            _repository.All().Returns(new List<ConfigModel>
            {
                new ConfigModel { Key = "MaxUploadSpeedKbps", Value = "0" }
            }.AsQueryable());

            Assert.That(_subject.MaxUploadSpeedKbps, Is.EqualTo(ConfigService.DefaultMaxUploadSpeedKbps));
        }

        [Test]
        public void AlternativeSpeedEnabled_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.AlternativeSpeedEnabled, Is.False);
        }

        [Test]
        public void AltUploadSpeedKbps_should_default_to_50()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.AltUploadSpeedKbps, Is.EqualTo(50));
        }

        [Test]
        public void AltDownloadSpeedKbps_should_default_to_100()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.AltDownloadSpeedKbps, Is.EqualTo(100));
        }

        [Test]
        public void GlobalSeedRatioLimit_should_default_to_zero()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.GlobalSeedRatioLimit, Is.EqualTo(0.0));
        }

        // ---- Property defaults (Speed Distribution) ----

        [Test]
        public void UploadDistributionAlgorithm_should_default_to_Equal()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UploadDistributionAlgorithm, Is.EqualTo("Equal"));
        }

        [Test]
        public void UploadDistributionSpreadPercentage_should_default_to_50()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UploadDistributionSpreadPercentage, Is.EqualTo(50));
        }

        [Test]
        public void UploadRedistributionMode_should_default_to_tick()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UploadRedistributionMode, Is.EqualTo("tick"));
        }

        [Test]
        public void UploadCustomIntervalMinutes_should_default_to_5()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UploadCustomIntervalMinutes, Is.EqualTo(5));
        }

        [Test]
        public void UploadStoppedMinPercentage_should_default_to_20()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UploadStoppedMinPercentage, Is.EqualTo(20));
        }

        [Test]
        public void UploadStoppedMaxPercentage_should_default_to_40()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UploadStoppedMaxPercentage, Is.EqualTo(40));
        }

        [Test]
        public void DownloadDistributionAlgorithm_should_default_to_Equal()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DownloadDistributionAlgorithm, Is.EqualTo("Equal"));
        }

        [Test]
        public void DownloadDistributionSpreadPercentage_should_default_to_50()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DownloadDistributionSpreadPercentage, Is.EqualTo(50));
        }

        [Test]
        public void DownloadRedistributionMode_should_default_to_tick()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DownloadRedistributionMode, Is.EqualTo("tick"));
        }

        [Test]
        public void DownloadCustomIntervalMinutes_should_default_to_5()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DownloadCustomIntervalMinutes, Is.EqualTo(5));
        }

        [Test]
        public void DownloadStoppedMinPercentage_should_default_to_20()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DownloadStoppedMinPercentage, Is.EqualTo(20));
        }

        [Test]
        public void DownloadStoppedMaxPercentage_should_default_to_40()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DownloadStoppedMaxPercentage, Is.EqualTo(40));
        }

        [Test]
        public void SpeedVariationMin_should_default_to_0_2()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.SpeedVariationMin, Is.EqualTo(0.2));
        }

        [Test]
        public void SpeedVariationMax_should_default_to_0_8()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.SpeedVariationMax, Is.EqualTo(0.8));
        }

        [Test]
        public void DownloadThresholdPercent_should_default_to_30()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DownloadThresholdPercent, Is.EqualTo(30));
        }

        // ---- Property defaults (Scheduler) ----

        [Test]
        public void SchedulerEnabled_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.SchedulerEnabled, Is.False);
        }

        [Test]
        public void SchedulerStartHour_should_default_to_22()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.SchedulerStartHour, Is.EqualTo(22));
        }

        [Test]
        public void SchedulerStartMinute_should_default_to_0()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.SchedulerStartMinute, Is.EqualTo(0));
        }

        [Test]
        public void SchedulerEndHour_should_default_to_6()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.SchedulerEndHour, Is.EqualTo(6));
        }

        [Test]
        public void SchedulerEndMinute_should_default_to_0()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.SchedulerEndMinute, Is.EqualTo(0));
        }

        [TestCase("SchedulerMonday", true)]
        [TestCase("SchedulerTuesday", true)]
        [TestCase("SchedulerWednesday", true)]
        [TestCase("SchedulerThursday", true)]
        [TestCase("SchedulerFriday", true)]
        [TestCase("SchedulerSaturday", true)]
        [TestCase("SchedulerSunday", true)]
        public void Scheduler_day_properties_should_default_to_true(string propertyName, bool expected)
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            var prop = typeof(ConfigService).GetProperty(propertyName);
            var result = (bool)prop.GetValue(_subject);

            Assert.That(result, Is.EqualTo(expected));
        }

        // ---- Property defaults (Peer Protocol) ----

        [Test]
        public void HandshakeTimeoutSeconds_should_default_to_30()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.HandshakeTimeoutSeconds, Is.EqualTo(30));
        }

        [Test]
        public void MessageReadTimeoutSeconds_should_default_to_60()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MessageReadTimeoutSeconds, Is.EqualTo(60));
        }

        [Test]
        public void KeepAliveIntervalSeconds_should_default_to_120()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.KeepAliveIntervalSeconds, Is.EqualTo(120));
        }

        [Test]
        public void PeerContactIntervalSeconds_should_default_to_300()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.PeerContactIntervalSeconds, Is.EqualTo(300));
        }

        [Test]
        public void UdpTrackerTimeoutSeconds_should_default_to_5()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UdpTrackerTimeoutSeconds, Is.EqualTo(5));
        }

        [Test]
        public void HttpTrackerTimeoutSeconds_should_default_to_10()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.HttpTrackerTimeoutSeconds, Is.EqualTo(10));
        }

        [Test]
        public void PeerRequestCount_should_default_to_200()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.PeerRequestCount, Is.EqualTo(200));
        }

        // ---- Property defaults (Peer Behavior) ----

        [Test]
        public void SeederUploadActivityProbability_should_default_to_0_85()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.SeederUploadActivityProbability, Is.EqualTo(0.85));
        }

        [Test]
        public void PeerIdleChance_should_default_to_0_3()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.PeerIdleChance, Is.EqualTo(0.3));
        }

        [Test]
        public void PeerDropoutProbability_should_default_to_0_1()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.PeerDropoutProbability, Is.EqualTo(0.1));
        }

        [Test]
        public void ConnectionRotationPercentage_should_default_to_0_25()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ConnectionRotationPercentage, Is.EqualTo(0.25));
        }

        // ---- Property defaults (Protocol Extensions) ----

        [Test]
        public void ExtensionUtMetadata_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ExtensionUtMetadata, Is.True);
        }

        [Test]
        public void ExtensionUtPex_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ExtensionUtPex, Is.True);
        }

        [Test]
        public void ExtensionLtDontHave_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ExtensionLtDontHave, Is.True);
        }

        [Test]
        public void ExtensionFastExtension_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.ExtensionFastExtension, Is.True);
        }

        [Test]
        public void UtpEnabled_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UtpEnabled, Is.False);
        }

        [Test]
        public void TcpFallback_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TcpFallback, Is.True);
        }

        [Test]
        public void TransportConnectionTimeoutSeconds_should_default_to_30()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TransportConnectionTimeoutSeconds, Is.EqualTo(30));
        }

        [Test]
        public void PexInterval_should_default_to_60()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.PexInterval, Is.EqualTo(60));
        }

        [Test]
        public void PexMaxPeersPerMessage_should_default_to_50()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.PexMaxPeersPerMessage, Is.EqualTo(50));
        }

        // ---- Property defaults (Multi-Tracker) ----

        [Test]
        public void MultiTrackerEnabled_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MultiTrackerEnabled, Is.True);
        }

        [Test]
        public void MultiTrackerFailoverEnabled_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.MultiTrackerFailoverEnabled, Is.True);
        }

        [Test]
        public void AnnounceToAllTiers_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.AnnounceToAllTiers, Is.False);
        }

        [Test]
        public void AnnounceToAllInTier_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.AnnounceToAllInTier, Is.False);
        }

        [Test]
        public void FailoverMaxConsecutiveFailures_should_default_to_5()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.FailoverMaxConsecutiveFailures, Is.EqualTo(5));
        }

        [Test]
        public void FailoverBackoffBaseSeconds_should_default_to_60()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.FailoverBackoffBaseSeconds, Is.EqualTo(60));
        }

        [Test]
        public void FailoverMaxBackoffSeconds_should_default_to_3600()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.FailoverMaxBackoffSeconds, Is.EqualTo(3600));
        }

        // ---- Property defaults (DHT) ----

        [Test]
        public void DhtRoutingTableSize_should_default_to_160()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtRoutingTableSize, Is.EqualTo(160));
        }

        [Test]
        public void DhtAnnouncementInterval_should_default_to_1800()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtAnnouncementInterval, Is.EqualTo(1800));
        }

        [Test]
        public void DhtBootstrapTimeout_should_default_to_30()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtBootstrapTimeout, Is.EqualTo(30));
        }

        [Test]
        public void DhtQueryTimeout_should_default_to_10()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtQueryTimeout, Is.EqualTo(10));
        }

        [Test]
        public void DhtMaxNodes_should_default_to_1000()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtMaxNodes, Is.EqualTo(1000));
        }

        [Test]
        public void DhtBucketSize_should_default_to_8()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtBucketSize, Is.EqualTo(8));
        }

        [Test]
        public void DhtConcurrentQueries_should_default_to_3()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtConcurrentQueries, Is.EqualTo(3));
        }

        [Test]
        public void DhtAutoBootstrap_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtAutoBootstrap, Is.True);
        }

        [Test]
        public void DhtRateLimitEnabled_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtRateLimitEnabled, Is.True);
        }

        [Test]
        public void DhtMaxQueriesPerSecond_should_default_to_10()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DhtMaxQueriesPerSecond, Is.EqualTo(10));
        }

        // ---- Property defaults (Tracker Server) ----

        [Test]
        public void TrackerServerEnabled_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerServerEnabled, Is.False);
        }

        [Test]
        public void TrackerHttpEnabled_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerHttpEnabled, Is.True);
        }

        [Test]
        public void TrackerHttpPort_should_default_to_9696()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerHttpPort, Is.EqualTo(9696));
        }

        [Test]
        public void TrackerUdpEnabled_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerUdpEnabled, Is.True);
        }

        [Test]
        public void TrackerUdpPort_should_default_to_6969()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerUdpPort, Is.EqualTo(6969));
        }

        [Test]
        public void TrackerBindAddress_should_default_to_0000()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerBindAddress, Is.EqualTo("0.0.0.0"));
        }

        [Test]
        public void TrackerAnnounceInterval_should_default_to_1800()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerAnnounceInterval, Is.EqualTo(1800));
        }

        [Test]
        public void TrackerMaxPeersPerAnnounce_should_default_to_50()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerMaxPeersPerAnnounce, Is.EqualTo(50));
        }

        [Test]
        public void TrackerEnableScrape_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerEnableScrape, Is.True);
        }

        [Test]
        public void TrackerPrivateMode_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerPrivateMode, Is.False);
        }

        [Test]
        public void TrackerLogAnnounces_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerLogAnnounces, Is.False);
        }

        [Test]
        public void TrackerRateLimitPerMinute_should_default_to_60()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.TrackerRateLimitPerMinute, Is.EqualTo(60));
        }

        // ---- Property defaults (Advanced/Logging) ----

        [Test]
        public void LogToFile_should_default_to_true()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.LogToFile, Is.True);
        }

        [Test]
        public void FileLogLevel_should_default_to_Info()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.FileLogLevel, Is.EqualTo("Info"));
        }

        [Test]
        public void DebugMode_should_default_to_false()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.DebugMode, Is.False);
        }

        [Test]
        public void UiRefreshRateSec_should_default_to_9()
        {
            _repository.All().Returns(new List<ConfigModel>().AsQueryable());

            Assert.That(_subject.UiRefreshRateSec, Is.EqualTo(9));
        }

        // ---- Property overrides (stored values override defaults) ----

        [Test]
        public void AutoStart_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "AutoStart", Value = "False" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.AutoStart, Is.False);
        }

        [Test]
        public void ListeningPort_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "ListeningPort", Value = "12345" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.ListeningPort, Is.EqualTo(12345));
        }

        [Test]
        public void MaxGlobalConnections_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "MaxGlobalConnections", Value = "500" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.MaxGlobalConnections, Is.EqualTo(500));
        }

        [Test]
        public void TrackerHttpPort_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "TrackerHttpPort", Value = "8080" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.TrackerHttpPort, Is.EqualTo(8080));
        }

        [Test]
        public void GlobalSeedRatioLimit_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "GlobalSeedRatioLimit", Value = "2.5" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.GlobalSeedRatioLimit, Is.EqualTo(2.5));
        }

        [Test]
        public void EncryptionMode_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "EncryptionMode", Value = "required" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.EncryptionMode, Is.EqualTo("required"));
        }

        [Test]
        public void TrackerServerEnabled_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "TrackerServerEnabled", Value = "True" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.TrackerServerEnabled, Is.True);
        }

        [Test]
        public void SchedulerEnabled_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "SchedulerEnabled", Value = "True" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.SchedulerEnabled, Is.True);
        }

        [Test]
        public void SpeedVariationMin_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "SpeedVariationMin", Value = "0.5" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.SpeedVariationMin, Is.EqualTo(0.5));
        }

        [Test]
        public void FileLogLevel_should_return_stored_value_when_set()
        {
            var configs = new List<ConfigModel>
            {
                new ConfigModel { Key = "FileLogLevel", Value = "Debug" }
            };
            _repository.All().Returns(configs.AsQueryable());

            Assert.That(_subject.FileLogLevel, Is.EqualTo("Debug"));
        }
    }
}
