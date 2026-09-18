import { useState, useEffect, useRef, Fragment, type CSSProperties } from "react";
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

interface ScheduledTaskHistoryItem {
  id: number;
  taskId: number;
  typeName: string;
  startedAt: string;
  finishedAt: string;
  durationMs: number;
  status: string | number;
  triggerSource: string | number;
  errorMessage: string | null;
  exceptionDetails: string | null;
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
    return isFuture ? "in less than a minute" : "just now";
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

function formatDurationMs(durationMs: number): string {
  if (durationMs < 1000) {
    return `${durationMs} ms`;
  }
  const seconds = (durationMs / 1000).toFixed(2);
  return `${seconds} s`;
}

function getHistoryStatusInfo(status: string | number): {
  label: string;
  badgeClass: string;
  style: CSSProperties;
} {
  const s = String(status).toLowerCase();
  if (s === "0" || s === "success") {
    return {
      label: "Success",
      badgeClass: "badge badge-seeding",
      style: {
        backgroundColor: "rgba(46, 204, 113, 0.2)",
        color: "#2ecc71",
        borderColor: "#2ecc71",
      },
    };
  }
  if (s === "1" || s === "failed") {
    return {
      label: "Failed",
      badgeClass: "badge badge-error",
      style: {
        backgroundColor: "rgba(231, 76, 60, 0.2)",
        color: "#e74c3c",
        borderColor: "#e74c3c",
      },
    };
  }
  if (s === "2" || s === "canceled" || s === "cancelled") {
    return {
      label: "Canceled",
      badgeClass: "badge badge-queued",
      style: {
        backgroundColor: "rgba(241, 196, 15, 0.2)",
        color: "#f1c40f",
        borderColor: "#f1c40f",
      },
    };
  }
  return {
    label: String(status),
    badgeClass: "badge",
    style: {},
  };
}

function formatTriggerSource(source: string | number): string {
  const s = String(source).toLowerCase();
  if (s === "0" || s === "scheduler") return "Scheduler";
  if (s === "1" || s === "manual") return "Manual";
  if (s === "2" || s === "api") return "API";
  return String(source);
}

interface TaskHistoryModalProps {
  task: ScheduledTask;
  onClose: () => void;
}

function TaskHistoryModal({ task, onClose }: TaskHistoryModalProps) {
  const [expandedIds, setExpandedIds] = useState<Set<number>>(new Set());

  const {
    data: history,
    isLoading,
    isError,
    refetch,
    isFetching,
  } = useQuery<ScheduledTaskHistoryItem[]>({
    queryKey: ["system", "task-history", task.id ?? task.typeName],
    queryFn: () => {
      const endpoint = task.id
        ? `/system/task/${task.id}/history?limit=50`
        : `/system/task/${encodeURIComponent(task.typeName)}/history?limit=50`;
      return apiClient.get(endpoint);
    },
    refetchInterval: 15000,
  });

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        onClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const toggleExpand = (id: number) => {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  return (
    <div
      className="modal-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) {
          onClose();
        }
      }}
      role="dialog"
      aria-modal="true"
      aria-labelledby="task-history-title"
    >
      <div
        className="modal"
        style={{
          maxWidth: "960px",
          width: "95%",
          maxHeight: "85vh",
          display: "flex",
          flexDirection: "column",
          padding: "1.5rem",
          boxShadow:
            "0 12px 40px rgba(0, 0, 0, 0.6), 0 2px 8px rgba(0, 0, 0, 0.3)",
          border: "1px solid var(--border-light)",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
            paddingBottom: "0.75rem",
            borderBottom: "1px solid var(--border-light)",
            flexWrap: "wrap",
            gap: "0.5rem",
          }}
        >
          <div>
            <h2
              id="task-history-title"
              className="modal-title"
              style={{
                margin: 0,
                fontSize: "1.25rem",
                fontWeight: 600,
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
              }}
            >
              <span>📜</span> Task History: {formatTaskName(task.typeName)}
            </h2>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginTop: "0.25rem",
                fontFamily: "monospace",
              }}
            >
              {task.typeName}
            </div>
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <button
              type="button"
              className="btn btn-outline"
              style={{ padding: "0.25rem 0.6rem", fontSize: "0.8rem" }}
              onClick={() => refetch()}
              disabled={isFetching}
              title="Refresh history"
            >
              {isFetching ? "🔄 Refreshing..." : "🔄 Refresh"}
            </button>
            <button
              type="button"
              className="btn btn-outline"
              style={{ padding: "0.25rem 0.6rem", fontSize: "0.85rem" }}
              onClick={onClose}
              title="Close modal"
              aria-label="Close"
            >
              ✕
            </button>
          </div>
        </div>

        {/* Body */}
        <div style={{ flex: 1, overflowY: "auto", minHeight: "200px" }}>
          {isLoading && (
            <p className="loading" style={{ padding: "1.5rem" }}>
              Loading execution history...
            </p>
          )}
          {!isLoading && isError && (
            <p className="error" style={{ padding: "1.5rem" }}>
              Failed to load task history.
            </p>
          )}
          {!isLoading && !isError && history && history.length === 0 && (
            <p
              className="torrent-table-empty"
              style={{ padding: "2rem", textAlign: "center" }}
            >
              No execution history recorded for this task yet.
            </p>
          )}
          {!isLoading && !isError && history && history.length > 0 && (
            <div className="torrent-table-wrapper">
              <table className="torrent-table">
                <thead>
                  <tr>
                    <th className="torrent-table-th">Status</th>
                    <th className="torrent-table-th">Started</th>
                    <th className="torrent-table-th">Finished</th>
                    <th className="torrent-table-th">Duration</th>
                    <th className="torrent-table-th">Trigger</th>
                    <th className="torrent-table-th">Details</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((item) => {
                    const statusInfo = getHistoryStatusInfo(item.status);
                    const isExpanded = expandedIds.has(item.id);
                    const hasError =
                      Boolean(item.errorMessage || item.exceptionDetails) ||
                      statusInfo.label === "Failed";

                    return (
                      <Fragment key={item.id}>
                        <tr className="torrent-table-row">
                          <td>
                            <span
                              className={statusInfo.badgeClass}
                              style={{
                                fontSize: "0.75rem",
                                padding: "0.2rem 0.5rem",
                                ...statusInfo.style,
                              }}
                            >
                              {statusInfo.label === "Success" && "✓ "}
                              {statusInfo.label === "Failed" && "✕ "}
                              {statusInfo.label === "Canceled" && "— "}
                              {statusInfo.label}
                            </span>
                          </td>
                          <td title={formatDateTime(item.startedAt)}>
                            {formatDateTime(item.startedAt)}
                            <div
                              style={{
                                fontSize: "0.75rem",
                                color: "var(--text-muted)",
                              }}
                            >
                              {formatRelativeTime(item.startedAt)}
                            </div>
                          </td>
                          <td title={formatDateTime(item.finishedAt)}>
                            {formatDateTime(item.finishedAt)}
                            <div
                              style={{
                                fontSize: "0.75rem",
                                color: "var(--text-muted)",
                              }}
                            >
                              {formatRelativeTime(item.finishedAt)}
                            </div>
                          </td>
                          <td>
                            <code style={{ fontSize: "0.85rem", fontWeight: 600 }}>
                              {formatDurationMs(item.durationMs)}
                            </code>
                          </td>
                          <td>
                            <span
                              className="badge badge-secondary"
                              style={{ fontSize: "0.75rem" }}
                            >
                              {formatTriggerSource(item.triggerSource)}
                            </span>
                          </td>
                          <td>
                            {hasError ? (
                              <button
                                type="button"
                                className="btn btn-outline"
                                style={{
                                  fontSize: "0.75rem",
                                  padding: "0.2rem 0.5rem",
                                  color: "var(--color-danger, #e55353)",
                                  borderColor: "var(--color-danger, #e55353)",
                                }}
                                onClick={() => toggleExpand(item.id)}
                              >
                                {isExpanded ? "Hide Error ▲" : "View Error ▼"}
                              </button>
                            ) : (
                              <span
                                style={{
                                  fontSize: "0.8rem",
                                  color: "var(--text-muted)",
                                }}
                              >
                                -
                              </span>
                            )}
                          </td>
                        </tr>
                        {isExpanded && hasError && (
                          <tr style={{ backgroundColor: "rgba(231, 76, 60, 0.05)" }}>
                            <td colSpan={6} style={{ padding: "0.75rem 1rem" }}>
                              {item.errorMessage && (
                                <div
                                  style={{
                                    marginBottom: item.exceptionDetails ? "0.5rem" : 0,
                                    color: "var(--color-danger, #e55353)",
                                    fontWeight: 500,
                                  }}
                                >
                                  <strong>Error:</strong> {item.errorMessage}
                                </div>
                              )}
                              {item.exceptionDetails && (
                                <pre
                                  style={{
                                    margin: 0,
                                    padding: "0.75rem",
                                    backgroundColor: "var(--bg-dark, #15181e)",
                                    borderRadius: "4px",
                                    border: "1px solid var(--border-light)",
                                    fontSize: "0.75rem",
                                    overflowX: "auto",
                                    whiteSpace: "pre-wrap",
                                    wordBreak: "break-all",
                                    color: "var(--text-muted, #ccc)",
                                    maxHeight: "220px",
                                  }}
                                >
                                  {item.exceptionDetails}
                                </pre>
                              )}
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {/* Footer */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginTop: "1rem",
            paddingTop: "0.75rem",
            borderTop: "1px solid var(--border-light)",
          }}
        >
          <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
            Showing {history?.length ?? 0} recent runs
          </span>
          <button
            type="button"
            className="btn btn-outline"
            style={{ padding: "0.35rem 0.8rem", fontSize: "0.85rem" }}
            onClick={onClose}
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

function SystemTasks() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const [selectedHistoryTask, setSelectedHistoryTask] = useState<ScheduledTask | null>(null);
  const [executingTasks, setExecutingTasks] = useState<Set<string>>(new Set());
  const timeoutIdsRef = useRef<number[]>([]);

  useEffect(() => {
    const timeouts = timeoutIdsRef.current;
    return () => {
      timeouts.forEach((id) => clearTimeout(id));
    };
  }, []);

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

  const {
    data: tasks,
    isLoading: tasksLoading,
    isError: tasksError,
  } = useQuery<ScheduledTask[]>({
    queryKey: ["system", "tasks"],
    queryFn: () => apiClient.get("/system/task"),
    retry: false,
    refetchInterval: (query) => {
      const data = query.state.data as ScheduledTask[] | undefined;
      const hasActiveCmd = commands?.some((c) => c.status === "queued" || c.status === "started");
      const hasRunning =
        (data && data.some((t) => t.isRunning)) ||
        executingTasks.size > 0 ||
        Boolean(hasActiveCmd);
      return hasRunning ? 3000 : 30000;
    },
  });

  const executeMutation = useMutation({
    mutationFn: (task: ScheduledTask) => {
      const endpoint = task.id
        ? `/system/task/${task.id}/execute`
        : `/system/task/${encodeURIComponent(task.typeName)}/execute`;
      return apiClient.post(endpoint, {});
    },
    onMutate: (task) => {
      setExecutingTasks((prev) => {
        const next = new Set(prev);
        next.add(task.typeName);
        if (task.id !== undefined) {
          next.add(String(task.id));
        }
        return next;
      });
    },
    onSuccess: (_, task) => {
      showToast(`Started execution of ${formatTaskName(task.typeName)}`, "success");
      queryClient.invalidateQueries({ queryKey: ["system", "tasks"] });
      queryClient.invalidateQueries({ queryKey: ["system", "commands"] });
      const timer = window.setTimeout(() => {
        setExecutingTasks((prev) => {
          const next = new Set(prev);
          next.delete(task.typeName);
          if (task.id !== undefined) {
            next.delete(String(task.id));
          }
          return next;
        });
      }, 3000);
      timeoutIdsRef.current.push(timer);
    },
    onError: (err: Error, task) => {
      setExecutingTasks((prev) => {
        const next = new Set(prev);
        next.delete(task.typeName);
        if (task.id !== undefined) {
          next.delete(String(task.id));
        }
        return next;
      });
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
      setExecutingTasks((prev) => {
        const next = new Set(prev);
        next.delete(task.typeName);
        if (task.id !== undefined) {
          next.delete(String(task.id));
        }
        return next;
      });
      queryClient.invalidateQueries({ queryKey: ["system", "tasks"] });
      queryClient.invalidateQueries({ queryKey: ["system", "commands"] });
    },
    onError: (err: Error) => {
      showToast(`Failed to abort task: ${err.message}`, "error");
    },
  });

  const isTaskRunning = (task: ScheduledTask): boolean => {
    if (
      executingTasks.has(task.typeName) ||
      (task.id !== undefined && executingTasks.has(String(task.id)))
    ) {
      return true;
    }
    if (
      executeMutation.isPending &&
      (executeMutation.variables?.typeName === task.typeName ||
        (task.id !== undefined && executeMutation.variables?.id === task.id))
    ) {
      return true;
    }
    if (task.isRunning) {
      return true;
    }
    if (task.lastStartTime) {
      if (!task.lastExecution) return true;
      const start = new Date(task.lastStartTime).getTime();
      const end = new Date(task.lastExecution).getTime();
      if (!isNaN(start) && !isNaN(end) && start > end) {
        return true;
      }
    }
    if (commands && commands.length > 0) {
      const shortName = task.typeName.includes(".")
        ? task.typeName.split(".").pop() || task.typeName
        : task.typeName;
      const hasActiveCommand = commands.some((cmd) => {
        const match =
          cmd.name === task.typeName ||
          cmd.name === shortName ||
          (task.name && cmd.name === task.name);
        return match && (cmd.status === "queued" || cmd.status === "started");
      });
      if (hasActiveCommand) return true;
    }
    return false;
  };

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
                  <th className="torrent-table-th">Status</th>
                  <th className="torrent-table-th">Execution Interval</th>
                  <th className="torrent-table-th">Last Execution</th>
                  <th className="torrent-table-th">Last Duration</th>
                  <th className="torrent-table-th">Next Execution</th>
                  <th className="torrent-table-th" style={{ textAlign: "right" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {tasks.map((task) => {
                  const running = isTaskRunning(task);
                  const isAborting =
                    abortTaskMutation.isPending &&
                    (abortTaskMutation.variables?.typeName === task.typeName ||
                      (task.id !== undefined && abortTaskMutation.variables?.id === task.id));
                  return (
                    <tr key={task.typeName} className="torrent-table-row">
                      <td>
                        <strong style={{ color: "var(--text-primary)" }}>
                          {formatTaskName(task.typeName)}
                        </strong>
                      </td>
                      <td>
                        {running ? (
                          <span
                            className="badge badge-seeding"
                            style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                          >
                            ⏳ Running
                          </span>
                        ) : (
                          <span
                            className="badge badge-secondary"
                            style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                          >
                            ● Idle
                          </span>
                        )}
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
                        {running && (
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
                          type="button"
                          className="btn btn-outline"
                          style={{
                            fontSize: "0.75rem",
                            padding: "0.25rem 0.6rem",
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.3rem",
                            marginRight: "0.5rem",
                          }}
                          onClick={() => setSelectedHistoryTask(task)}
                          title="View task execution history"
                        >
                          📜 History
                        </button>
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
                          disabled={running}
                          title={running ? "Task is currently running" : "Execute task now"}
                        >
                          {running ? "⏳ Running..." : "⚡ Run Now"}
                        </button>
                        {running && (
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
                            disabled={isAborting}
                            title="Abort running task"
                          >
                            {isAborting ? "🛑 Aborting..." : "🛑 Abort"}
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
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

      {selectedHistoryTask && (
        <TaskHistoryModal
          task={selectedHistoryTask}
          onClose={() => setSelectedHistoryTask(null)}
        />
      )}
    </div>
  );
}

export default SystemTasks;
