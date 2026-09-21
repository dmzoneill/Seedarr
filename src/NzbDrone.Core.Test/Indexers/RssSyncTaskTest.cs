using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class RssSyncTaskTest
{
    [Test]
    public void DefaultInterval_is_15_minutes()
    {
        var syncService = Substitute.For<IRssSyncService>();
        var task = new RssSyncTask(syncService);

        Assert.That(task.DefaultInterval, Is.EqualTo(15));
    }

    [Test]
    public void Execute_invokes_Sync_with_isManual_false()
    {
        var syncService = Substitute.For<IRssSyncService>();
        syncService.Sync(false).Returns(2);
        var task = new RssSyncTask(syncService);

        task.Execute();

        syncService.Received(1).Sync(false);
    }
}
