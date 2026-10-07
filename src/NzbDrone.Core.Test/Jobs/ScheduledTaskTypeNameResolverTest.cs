using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Test.Jobs;

[TestFixture]
public class ScheduledTaskTypeNameResolverTest
{
    private class SampleTask : IScheduledTask
    {
        public int DefaultInterval => 1;

        public void Execute(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    [Test]
    public void ResolveForSignalR_should_prefer_task_instance_full_name()
    {
        IScheduledTask task = new SampleTask();
        var result = ScheduledTaskTypeNameResolver.ResolveForSignalR("SampleTask", task, new List<IScheduledTask>());

        Assert.That(result, Is.EqualTo(typeof(SampleTask).FullName));
    }

    [Test]
    public void Normalize_should_resolve_short_name_from_registered_tasks()
    {
        var tasks = new List<IScheduledTask> { new SampleTask() };
        var result = ScheduledTaskTypeNameResolver.Normalize("SampleTask", tasks, null);

        Assert.That(result, Is.EqualTo(typeof(SampleTask).FullName));
    }
}
