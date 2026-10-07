// Copyright (c) FeedItOut. All rights reserved.

using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Developer;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperSyntheticEventPublisherTest
{
    [Test]
    public void TryCreateEvent_should_resolve_event_name_without_event_suffix()
    {
        Assert.That(
            DeveloperSyntheticEventPublisher.TryCreateEvent("TorrentImported", "{}", out var evt, out var error),
            Is.True);
        Assert.That(error, Is.Null);
        Assert.That(evt, Is.TypeOf<TorrentImportedEvent>());
    }

    [Test]
    public void TryCreateEvent_should_block_lifecycle_events()
    {
        Assert.That(
            DeveloperSyntheticEventPublisher.TryCreateEvent("ApplicationStartedEvent", "{}", out _, out var error),
            Is.False);
        Assert.That(error, Does.Contain("not allowed"));
    }

    [Test]
    public void TryCreateEvent_should_deserialize_config_saved_event()
    {
        Assert.That(
            DeveloperSyntheticEventPublisher.TryCreateEvent("ConfigSaved", "{}", out var evt, out _),
            Is.True);
        Assert.That(evt, Is.TypeOf<ConfigSavedEvent>());
    }
}
