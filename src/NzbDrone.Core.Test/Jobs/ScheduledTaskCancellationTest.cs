using System.Collections.Generic;
using System.Threading;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Test.Jobs;

[TestFixture]
public class ScheduledTaskCancellationTest
{
    [Test]
    public void DatabaseVacuumTask_should_not_run_maintenance_when_already_canceled()
    {
        var maintenance = Substitute.For<IDatabaseMaintenanceService>();
        maintenance.GetFreelistCount().Returns(100);
        maintenance.IsIncrementalAutoVacuumEnabled().Returns(true);

        var task = new DatabaseVacuumTask(maintenance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => task.Execute(cts.Token));
        maintenance.DidNotReceive().PerformMaintenance(Arg.Any<int?>());
    }

    [Test]
    public void RssSyncService_should_stop_between_indexers_when_canceled()
    {
        var indexerRepo = Substitute.For<IIndexerRepository>();
        indexerRepo.All().Returns(new List<IndexerDefinition>
        {
            new() { Id = 1, Name = "A", Enable = true, EnableRss = true, IndexerType = "torznab", Implementation = "TorznabIndexer" },
            new() { Id = 2, Name = "B", Enable = true, EnableRss = true, IndexerType = "torznab", Implementation = "TorznabIndexer" }
        });

        var service = new RssSyncService(indexerRepository: indexerRepo);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => service.Sync(cancellationToken: cts.Token));
    }
}
