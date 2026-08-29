import { useQuery } from "@tanstack/react-query";
import { apiClient } from "../api/client";

interface ScheduledTask {
  typeName: string;
  interval: number;
  lastExecution: string | null;
  lastStartTime: string | null;
  lastDuration: string | null;
  nextExecution: string | null;
}

interface CommandItem {
  id: number;
  name: string;
  status: string;
  queuedAt: string;
  startedAt: string | null;
  endedAt: string | null;
  duration: string | null;
  message: string | null;
}

function formatTaskName(typeName: string): string {
  if (!typeName) return "";
  const shortName = typeName.includes(".")
    ? typeName.split(".").pop() || typeName
    : typeName;
  return shortName.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatInterval(minutes: number): string {
  if (minutes < 60) {
    return `${minutes} minute${minutes !== 1 ? "s" : ""}`;
  }
  const hours = Math.floor(minutes / 60);
  if (minutes % 60 === 0) {
    if (hours >= 24 && hours % 24 === 0) {
      const days = hours / 24;
      return `${days} day${days !== 1 ? "s" : ""}`;
    }
    return `${hours} hour${hours !== 1 ? "s" : ""}`;
  }
  return `${hours}h ${minutes % 60}m`;
}

function formatRelativeTime(dateStr: string | null): string {
  if (!dateStr) return "-";
  const date = new Date(dateStr);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const absDiff = Math.abs(diffMs);
  const isFuture = diffMs < 0;

  const seconds = Math.floor(absDiff / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  let text: string;
  if (seconds < 60) {
    text = "just now";
    return text;
  } else if (minutes < 60) {
    text = `${minutes} minute${minutes !== 1 ? "s" : ""}`;
  } else if (hours < 24) {
    text = `${hours} hour${hours !== 1 ? "s" : ""}`;
  } else {
    text = `${days} day${days !== 1 ? "s" : ""}`;
  }

  return isFuture ? `in ${text}` : `${text} ago`;
}

function formatDuration(durationStr: string | null): string {
  if (!durationStr) return "-";
  const match = durationStr.match(/^(\d+):(\d+):(\d+)/);
  if (!match) return durationStr;
  const [, h, m, s] = match;
  const hours = parseInt(h, 10);
  const minutes = parseInt(m, 10);
  const seconds = parseInt(s, 10);
  if (hours > 0) return `${hours}h ${minutes}m ${seconds}s`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
}

function formatDateTime(dateStr: string | null): string {
  if (!dateStr) return "-";
  return new Date(dateStr).toLocaleString();
}

function statusIcon(status: string): string {
  switch (status) {
    case "queued":
      return "⌚";
    case "started":
      return "⏳";
    case "completed":
      return "✓";
    case "failed":
      return "✗";
    case "cancelled":
      return "—";
    default:
      return "";
  }
}

function statusClass(status: string): string {
  switch (status) {
    case "queued":
      return "badge badge-queued";
    case "started":
      return "badge badge-seeding";
    case "completed":
      return "badge badge-success";
    case "failed":
      return "badge badge-error";
    case "cancelled":
      return "badge badge-stopped";
    default:
      return "badge";
  }
}

function SystemTasks() {
  const {
    data: tasks,
    isLoading: tasksLoading,
    isError: tasksError,
  } = useQuery<ScheduledTask[]>({
    queryKey: ["system", "tasks"],
    queryFn: () => apiClient.get("/system/task"),
    retry: false,
    refetchInterval: 30000,
  });

  const {
    data: commands,
    isLoading: commandsLoading,
    isError: commandsError,
  } = useQuery<CommandItem[]>({
    queryKey: ["system", "commands"],
    queryFn: () => apiClient.get("/system/command"),
    retry: false,
    refetchInterval: 5000,
  });

  return (
    <div>
      <h1 className="page-heading">Tasks</h1>

      {/* Scheduled Tasks Section */}
      <div className="card">
        <h3>Scheduled</h3>
        {tasksLoading && <p className="loading">Loading tasks...</p>}
        {!tasksLoading && tasksError && (
          <p className="error">Failed to load tasks.</p>
        )}
        {tasks && tasks.length > 0 && (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Name</th>
                  <th className="torrent-table-th">Interval</th>
                  <th className="torrent-table-th">Last Execution</th>
                  <th className="torrent-table-th">Last Duration</th>
                  <th className="torrent-table-th">Next Execution</th>
                </tr>
              </thead>
              <tbody>
                {tasks.map((task) => (
                  <tr key={task.typeName} className="torrent-table-row">
                    <td>{formatTaskName(task.typeName)}</td>
                    <td>{formatInterval(task.interval)}</td>
                    <td title={formatDateTime(task.lastExecution)}>
                      {formatRelativeTime(task.lastExecution)}
                    </td>
                    <td>{formatDuration(task.lastDuration)}</td>
                    <td title={formatDateTime(task.nextExecution)}>
                      {formatRelativeTime(task.nextExecution)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {tasks && tasks.length === 0 && (
          <p className="torrent-table-empty">No scheduled tasks.</p>
        )}
      </div>

      {/* Command Queue Section */}
      <div className="card">
        <h3>Queue</h3>
        {commandsLoading && <p className="loading">Loading commands...</p>}
        {!commandsLoading && commandsError && (
          <p className="error">Failed to load commands.</p>
        )}
        {commands && commands.length > 0 && (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Name</th>
                  <th className="torrent-table-th">Queued</th>
                  <th className="torrent-table-th">Started</th>
                  <th className="torrent-table-th">Ended</th>
                  <th className="torrent-table-th">Duration</th>
                </tr>
              </thead>
              <tbody>
                {commands.map((cmd) => (
                  <tr key={cmd.id} className="torrent-table-row">
                    <td>
                      <span
                        className={statusClass(cmd.status)}
                        style={{ marginRight: "0.5rem" }}
                      >
                        {statusIcon(cmd.status)} {cmd.status}
                      </span>
                      {formatTaskName(cmd.name)}
                      {cmd.message && (
                        <span
                          className="status-value"
                          style={{ marginLeft: "0.5rem", fontSize: "0.8em" }}
                        >
                          {cmd.message}
                        </span>
                      )}
                    </td>
                    <td title={formatDateTime(cmd.queuedAt)}>
                      {formatRelativeTime(cmd.queuedAt)}
                    </td>
                    <td title={formatDateTime(cmd.startedAt)}>
                      {formatRelativeTime(cmd.startedAt)}
                    </td>
                    <td title={formatDateTime(cmd.endedAt)}>
                      {formatRelativeTime(cmd.endedAt)}
                    </td>
                    <td>{formatDuration(cmd.duration)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {commands && commands.length === 0 && (
          <p className="torrent-table-empty">No recent commands.</p>
        )}
      </div>
    </div>
  );
}

export default SystemTasks;
