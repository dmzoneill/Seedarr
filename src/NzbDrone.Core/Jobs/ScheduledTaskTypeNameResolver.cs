using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Jobs;

public static class ScheduledTaskTypeNameResolver
{
    public static string ResolveForSignalR(string typeName, IScheduledTask taskInstance, IEnumerable<IScheduledTask> scheduledTasks)
    {
        if (taskInstance != null)
        {
            return taskInstance.GetType().FullName;
        }

        return Normalize(typeName, scheduledTasks, null);
    }

    public static string Normalize(
        string typeName,
        IEnumerable<IScheduledTask> scheduledTasks,
        IEnumerable<ScheduledTask> dbTasks)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return typeName;
        }

        var scheduledMatch = scheduledTasks?.FirstOrDefault(t =>
            string.Equals(t.GetType().FullName, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase));

        if (scheduledMatch != null)
        {
            return scheduledMatch.GetType().FullName;
        }

        var dbMatch = dbTasks?.FirstOrDefault(t =>
            string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.TypeName.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (dbMatch != null)
        {
            return dbMatch.TypeName;
        }

        return typeName;
    }
}
