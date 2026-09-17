import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../api/client";
import { useToast } from "../context/ToastContext";

interface ScheduledTask {
  id?: number;
  typeName: string;
  name?: string;
  interval: number;
  lastExecution: string | null;
  lastStartTime: string | null;
  lastDuration: string | null;
  nextExecution: string | null;
  isRunning?: boolean;
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

function formatRelativeTime(dateStr: string | null, isNextExecution = false): string {
  if (!dateStr) return "-";
  const date = new Date(dateStr);
  if (isNaN(date.getTime()) || date.getFullYear() <= 1970) return "-";
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const absDiff = Math.abs(diffMs);
  const isFuture = diffMs < 0;

  if (isNextExecution && !isFuture) {
    return "now";
  }

  const seconds = Math.floor(absDiff / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  let text: string;
  if (seconds < 60) {
    text = "just now";
    return isFuture ? "in < 1 min" : text;
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
  const date = new Date(dateStr);
  if (isNaN(date.getTime()) || date.getFullYear() <= 1970) return "-";
  return date.toLocaleString();
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
      return "✕";
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
  const queryClient = useQueryClient();
  const { showToast } = useToast();

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

  const executeMutation = useMutation({
    mutationFn: (task: ScheduledTask) => {
      const endpoint = task.id
        ? `/system/task/${task.id}/execute`
        : `/system/task/${encodeURIComponent(task.typeName)}/execute`;
      return apiClient.post(endpoint, {});
    },
    onSuccess: (_, task) => {
      showToast(`Started execution of ${formatTaskName(task.typeName)}`, "success");
      queryClient.invalidateQueries({ queryKey: ["system", "tasks"] });
      queryClient.invalidateQueries({ queryKey: ["system", "commands"] });
    },
    onError: (err: Error) => {
      showToast(`Failed to execute task: ${err.message}`, "error");
    },
  });

  const cancelMutation = useMutation({
    mutationFn: (commandId: number) =>
      apiClient.delete(`/system/command/${commandId}`),
    onSuccess: () => {
      showToast("Command cancelled", "success");
      queryClient.invalidateQueries({ queryKey: ["system", "commands"] });
      queryClient.invalidateQueries({ queryKey: ["system", "tasks"] });
    },
    onError: (err: Error) => {
      showToast(`Failed to cancel command: ${err.message}`, "error");
    },
  });

  const abortTaskMutation = useMutation({
    mutationFn: (task: ScheduledTask) => {
      const endpoint = task.id
        ? `/system/task/${task.id}/abort`
        : `/system/task/${encodeURIComponent(task.typeName)}/abort`;
      return apiClient.post(endpoint, {});
    },
    onSuccess: (_, task) => {
      showToast(`Aborted execution of ${formatTaskName(task.typeName)}`, "success");
      queryClient.invalidateQueries({ queryKey: ["system", "tasks"] });
      queryClient.invalidateQueries({ queryKey: ["system", "commands"] });
    },
    onError: (err: Error) => {
      showToast(`Failed to abort task: ${err.message}`, "error");
    },
  });

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>⏱️</span> System: Tasks
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Scheduled background maintenance jobs, integration sync intervals, and command execution queue
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          <span
            className="badge badge-primary"
            style={{ padding: "0.35rem 0.75rem", fontSize: "0.85rem" }}
          >
            Active Jobs: {tasks?.length ?? 0}
          </span>
        </div>
      </div>

      {/* Scheduled Tasks Section Card */}
      <div
        className="card"
        style={{
          marginBottom: "1.25rem",
          borderRadius: "8px",
          border: "1px solid var(--border-light)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            padding: "1.1rem 1.25rem 0.85rem",
            borderBottom: "1px solid var(--border-light)",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              margin: 0,
            }}
          >
            Scheduled Background Tasks
          </h2>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Periodic routines maintaining torrent swarm state, webhook sync, and
            system cleanup
          </div>
        </div>

        {tasksLoading && (
          <p className="loading" style={{ padding: "1.25rem" }}>
            Loading tasks...
          </p>
        )}
        {!tasksLoading && tasksError && (
          <p className="error" style={{ padding: "1.25rem" }}>
            Failed to load tasks.
          </p>
        )}
        {tasks && tasks.length > 0 && (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Task Name</th>
                  <th className="torrent-table-th">Execution Interval</th>
                  <th className="torrent-table-th">Last Execution</th>
                  <th className="torrent-table-th">Last Duration</th>
                  <th className="torrent-table-th">Next Execution</th>
                  <th className="torrent-table-th" style={{ textAlign: "right" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {tasks.map((task) => (
                  <tr key={task.typeName} className="torrent-table-row">
                    <td>
                      <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                        <strong style={{ color: "var(--text-primary)" }}>
                          {formatTaskName(task.typeName)}
                        </strong>
                        {task.isRunning && (
                          <span
                            className="badge badge-seeding"
                            style={{ fontSize: "0.7rem", padding: "0.15rem 0.4rem" }}
                          >
                            ⏳ Running
                          </span>
                        )}
                      </div>
                    </td>
                    <td>
                      <span className="badge badge-secondary">
                        ⏱️ {formatInterval(task.interval)}
                      </span>
                    </td>
                    <td title={formatDateTime(task.lastExecution)}>
                      {formatRelativeTime(task.lastExecution)}
                    </td>
                    <td>
                      <code style={{ fontSize: "0.8rem" }}>
                        {formatDuration(task.lastDuration)}
                      </code>
                      {task.isRunning && (
                        <span
                          style={{
                            fontSize: "0.75rem",
                            color: "var(--text-muted)",
                            marginLeft: "0.3rem",
                          }}
                        >
                          (elapsed)
                        </span>
                      )}
                    </td>
                    <td
                      title={formatDateTime(task.nextExecution)}
                      style={{
                        color: "var(--accent, #c8a84e)",
                        fontWeight: 500,
                      }}
                    >
                      {formatRelativeTime(task.nextExecution, true)}
                    </td>
                    <td style={{ textAlign: "right", whiteSpace: "nowrap" }}>
                      <button
                        className="btn btn-outline"
                        style={{
                          fontSize: "0.75rem",
                          padding: "0.25rem 0.6rem",
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.3rem",
                        }}
                        onClick={() => executeMutation.mutate(task)}
                        disabled={task.isRunning || executeMutation.isPending}
                        title={task.isRunning ? "Task is currently running" : "Execute task now"}
                      >
                        {task.isRunning ? "⏳ Running..." : "⚡ Run Now"}
                      </button>
                      {task.isRunning && (
                        <button
                          className="btn btn-danger"
                          style={{
                            fontSize: "0.75rem",
                            padding: "0.25rem 0.6rem",
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.3rem",
                            marginLeft: "0.5rem",
                          }}
                          onClick={() => abortTaskMutation.mutate(task)}
                          disabled={abortTaskMutation.isPending}
                          title="Abort running task"
                        >
                          🛑 Abort
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {tasks && tasks.length === 0 && (
          <p className="torrent-table-empty" style={{ padding: "1.5rem" }}>
            No scheduled tasks registered.
          </p>
        )}
      </div>

      {/* Command Queue Section Card */}
      <div
        className="card"
        style={{
          borderRadius: "8px",
          border: "1px solid var(--border-light)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            padding: "1.1rem 1.25rem 0.85rem",
            borderBottom: "1px solid var(--border-light)",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              margin: 0,
            }}
          >
            Command Execution Queue
          </h2>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            History and progress of interactive and scheduled command runs
          </div>
        </div>

        {commandsLoading && (
          <p className="loading" style={{ padding: "1.25rem" }}>
            Loading command history...
          </p>
        )}
        {!commandsLoading && commandsError && (
          <p className="error" style={{ padding: "1.25rem" }}>
            Failed to load commands.
          </p>
        )}
        {commands && commands.length > 0 && (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Command / Status</th>
                  <th className="torrent-table-th">Queued</th>
                  <th className="torrent-table-th">Started</th>
                  <th className="torrent-table-th">Ended</th>
                  <th className="torrent-table-th">Duration</th>
                  <th className="torrent-table-th" style={{ textAlign: "right" }}>Actions</th>
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
                      <strong style={{ color: "var(--text-primary)" }}>
                        {formatTaskName(cmd.name)}
                      </strong>
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
                    <td>
                      <code style={{ fontSize: "0.8rem" }}>
                        {formatDuration(cmd.duration)}
                      </code>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      {(cmd.status === "queued" || cmd.status === "started") && (
                        <button
                          className="btn btn-outline"
                          style={{
                            fontSize: "0.75rem",
                            padding: "0.2rem 0.5rem",
                            color: "var(--color-danger, #e55353)",
                            borderColor: "var(--color-danger, #e55353)",
                          }}
                          onClick={() => cancelMutation.mutate(cmd.id)}
                          disabled={cancelMutation.isPending}
                          title="Cancel command"
                        >
                          ✕ Cancel
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {commands && commands.length === 0 && (
          <p className="torrent-table-empty" style={{ padding: "1.5rem" }}>
            No recent command executions in queue.
          </p>
        )}
      </div>
    </div>
  );
}

export default SystemTasks;
