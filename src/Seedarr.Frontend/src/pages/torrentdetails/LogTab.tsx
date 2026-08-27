import { useState, useMemo } from "react";
import { Torrent } from "../../api/types";
import { useTorrentLogs } from "../../api/hooks";
import { formatDate } from "../../utils/formatters";
import { StatusRow } from "./shared";

function levelBadgeClass(level: string): string {
  switch (level.toLowerCase()) {
    case "debug":
    case "trace":
      return "torrent-log-level-debug";
    case "warn":
    case "warning":
      return "torrent-log-level-warn";
    case "error":
    case "fatal":
      return "torrent-log-level-error";
    default:
      return "torrent-log-level-info";
  }
}

function sourceBadgeStyle(source: string): React.CSSProperties {
  switch (source.toLowerCase()) {
    case "tracker":
      return { backgroundColor: "rgba(59, 130, 246, 0.2)", color: "#60a5fa", borderColor: "rgba(59, 130, 246, 0.4)" };
    case "peers":
    case "peer":
      return { backgroundColor: "rgba(168, 85, 247, 0.2)", color: "#c084fc", borderColor: "rgba(168, 85, 247, 0.4)" };
    case "seeding":
    case "seeder":
      return { backgroundColor: "rgba(34, 197, 94, 0.2)", color: "#4ade80", borderColor: "rgba(34, 197, 94, 0.4)" };
    case "trackerboost":
      return { backgroundColor: "rgba(245, 158, 11, 0.2)", color: "#fbbf24", borderColor: "rgba(245, 158, 11, 0.4)" };
    default:
      return { backgroundColor: "rgba(148, 163, 184, 0.2)", color: "#cbd5e1", borderColor: "rgba(148, 163, 184, 0.4)" };
  }
}

export function LogTab({ torrent }: { torrent: Torrent }) {
  const [isLive, setIsLive] = useState(true);
  const [levelFilter, setLevelFilter] = useState<string>("ALL");
  const [sourceFilter, setSourceFilter] = useState<string>("ALL");
  const [searchTerm, setSearchTerm] = useState<string>("");
  const [copied, setCopied] = useState(false);

  const { data: rawLogs, isLoading, isError, refetch } = useTorrentLogs(torrent.id, {
    polling: isLive,
  });

  // Limit strictly to the latest 100 entries and sort most recent first
  const logs = useMemo(() => {
    return (rawLogs ?? []).slice(0, 100);
  }, [rawLogs]);

  const sources = useMemo(() => {
    const set = new Set<string>();
    for (const log of logs) {
      if (log.source) set.add(log.source);
    }
    return Array.from(set).sort();
  }, [logs]);

  const filteredLogs = useMemo(() => {
    return logs.filter((entry) => {
      if (levelFilter !== "ALL" && entry.level.toUpperCase() !== levelFilter) {
        return false;
      }
      if (sourceFilter !== "ALL" && entry.source.toLowerCase() !== sourceFilter.toLowerCase()) {
        return false;
      }
      if (searchTerm) {
        const query = searchTerm.toLowerCase();
        const matchMsg = entry.message.toLowerCase().includes(query);
        const matchSrc = entry.source.toLowerCase().includes(query);
        const matchLvl = entry.level.toLowerCase().includes(query);
        if (!matchMsg && !matchSrc && !matchLvl) return false;
      }
      return true;
    });
  }, [logs, levelFilter, sourceFilter, searchTerm]);

  function copyLogsToClipboard() {
    const text = filteredLogs
      .map(
        (l) =>
          `[${formatDate(l.timeStamp)}] [${l.level.toUpperCase()}] [${l.source}] ${l.message}`,
      )
      .join("\n");
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  return (
    <div className="card" style={{ display: "flex", flexDirection: "column", gap: "0.85rem" }}>
      {/* Header & Controls Toolbar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.6rem",
          paddingBottom: "0.5rem",
          borderBottom: "1px solid var(--border-light, rgba(255,255,255,0.08))",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}>
          <h3 style={{ margin: 0, fontSize: "1.1rem" }}>Seeder & Tracker Log</h3>
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.3rem",
              fontSize: "0.75rem",
              fontWeight: 500,
              padding: "0.2rem 0.5rem",
              borderRadius: "12px",
              backgroundColor: isLive ? "rgba(34, 197, 94, 0.15)" : "rgba(148, 163, 184, 0.15)",
              color: isLive ? "#4ade80" : "#94a3b8",
              border: `1px solid ${isLive ? "rgba(34, 197, 94, 0.3)" : "rgba(148, 163, 184, 0.3)"}`,
            }}
          >
            <span
              style={{
                width: "6px",
                height: "6px",
                borderRadius: "50%",
                backgroundColor: isLive ? "#22c55e" : "#94a3b8",
                boxShadow: isLive ? "0 0 6px #22c55e" : "none",
              }}
            />
            {isLive ? "Live (3s)" : "Paused"}
          </span>
          <span style={{ fontSize: "0.78rem", color: "var(--text-dim, #888)" }}>
            Showing {filteredLogs.length} of latest {logs.length} (capped at 100)
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}>
          <button
            className={`btn btn-sm ${isLive ? "btn-outline" : "btn-primary"}`}
            style={{ fontSize: "0.78rem", padding: "0.25rem 0.6rem" }}
            onClick={() => setIsLive(!isLive)}
            title={isLive ? "Pause auto-polling" : "Resume live updates"}
          >
            {isLive ? "⏸ Pause" : "▶ Resume"}
          </button>
          <button
            className="btn btn-outline btn-sm"
            style={{ fontSize: "0.78rem", padding: "0.25rem 0.6rem" }}
            onClick={() => refetch()}
            title="Refresh logs immediately"
          >
            🔄 Refresh
          </button>
          <button
            className="btn btn-outline btn-sm"
            style={{ fontSize: "0.78rem", padding: "0.25rem 0.6rem" }}
            onClick={copyLogsToClipboard}
            title="Copy logs to clipboard"
          >
            {copied ? "✓ Copied!" : "📋 Copy Log"}
          </button>
        </div>
      </div>

      {/* Filter & Search Bar */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: "0.75rem",
          flexWrap: "wrap",
        }}
      >
        {/* Search input */}
        <div style={{ flex: "1 1 200px", minWidth: "180px" }}>
          <input
            type="text"
            className="form-control form-control-sm"
            placeholder="Search log messages, trackers, peers..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            style={{
              width: "100%",
              padding: "0.3rem 0.6rem",
              fontSize: "0.8rem",
              borderRadius: "4px",
              backgroundColor: "var(--bg-primary)",
              color: "inherit",
              border: "1px solid var(--border-light, rgba(255,255,255,0.1))",
            }}
          />
        </div>

        {/* Level Filters */}
        <div style={{ display: "flex", gap: "0.25rem", alignItems: "center" }}>
          <span style={{ fontSize: "0.75rem", color: "var(--text-dim)" }}>Level:</span>
          {["ALL", "INFO", "DEBUG", "WARN", "ERROR"].map((lvl) => (
            <button
              key={lvl}
              className={`btn btn-sm ${levelFilter === lvl ? "btn-primary" : "btn-outline"}`}
              style={{
                fontSize: "0.72rem",
                padding: "0.18rem 0.45rem",
                borderRadius: "4px",
              }}
              onClick={() => setLevelFilter(lvl)}
            >
              {lvl}
            </button>
          ))}
        </div>

        {/* Source Filters */}
        {sources.length > 0 && (
          <div style={{ display: "flex", gap: "0.25rem", alignItems: "center" }}>
            <span style={{ fontSize: "0.75rem", color: "var(--text-dim)" }}>Source:</span>
            <button
              className={`btn btn-sm ${sourceFilter === "ALL" ? "btn-primary" : "btn-outline"}`}
              style={{
                fontSize: "0.72rem",
                padding: "0.18rem 0.45rem",
                borderRadius: "4px",
              }}
              onClick={() => setSourceFilter("ALL")}
            >
              All
            </button>
            {sources.map((src) => (
              <button
                key={src}
                className={`btn btn-sm ${sourceFilter === src ? "btn-primary" : "btn-outline"}`}
                style={{
                  fontSize: "0.72rem",
                  padding: "0.18rem 0.45rem",
                  borderRadius: "4px",
                }}
                onClick={() => setSourceFilter(src)}
              >
                {src}
              </button>
            ))}
          </div>
        )}
      </div>

      {/* Log Entries Table */}
      <div
        className="torrent-table-wrapper"
        style={{
          maxHeight: "520px",
          overflowY: "auto",
          backgroundColor: "#0d1117",
          borderRadius: "6px",
          border: "1px solid rgba(255,255,255,0.08)",
        }}
      >
        <table className="torrent-table" style={{ fontSize: "0.8rem", width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr style={{ backgroundColor: "#161b22", borderBottom: "1px solid rgba(255,255,255,0.1)" }}>
              <th className="torrent-table-th" style={{ width: "160px", padding: "0.4rem 0.6rem" }}>Timestamp</th>
              <th className="torrent-table-th" style={{ width: "80px", padding: "0.4rem 0.6rem" }}>Level</th>
              <th className="torrent-table-th" style={{ width: "110px", padding: "0.4rem 0.6rem" }}>Source</th>
              <th className="torrent-table-th" style={{ padding: "0.4rem 0.6rem" }}>Event Details</th>
            </tr>
          </thead>
          <tbody style={{ fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace" }}>
            {isLoading && logs.length === 0 ? (
              <tr className="torrent-table-row">
                <td colSpan={4} style={{ color: "var(--text-dim)", textAlign: "center", padding: "2rem" }}>
                  Loading latest seeder & tracker log entries...
                </td>
              </tr>
            ) : isError && logs.length === 0 ? (
              <tr className="torrent-table-row">
                <td colSpan={4} style={{ color: "var(--danger, #ef4444)", textAlign: "center", padding: "2rem" }}>
                  Failed to load seeder log entries
                </td>
              </tr>
            ) : filteredLogs.length === 0 ? (
              <tr className="torrent-table-row">
                <td colSpan={4} style={{ color: "var(--text-dim)", textAlign: "center", padding: "2rem" }}>
                  {logs.length === 0
                    ? "No seeder or tracker events recorded yet"
                    : "No log events match current search/filter criteria"}
                </td>
              </tr>
            ) : (
              filteredLogs.map((entry) => (
                <tr
                  key={entry.id}
                  className="torrent-table-row"
                  style={{
                    borderBottom: "1px solid rgba(255,255,255,0.04)",
                    backgroundColor:
                      entry.level.toUpperCase() === "ERROR"
                        ? "rgba(239, 68, 68, 0.08)"
                        : entry.level.toUpperCase() === "WARN"
                          ? "rgba(245, 158, 11, 0.08)"
                          : "transparent",
                  }}
                >
                  <td style={{ color: "#8b949e", whiteSpace: "nowrap", padding: "0.35rem 0.6rem" }}>
                    {formatDate(entry.timeStamp)}
                  </td>
                  <td style={{ padding: "0.35rem 0.6rem" }}>
                    <span className={`torrent-log-level ${levelBadgeClass(entry.level)}`}>
                      {entry.level.toUpperCase()}
                    </span>
                  </td>
                  <td style={{ padding: "0.35rem 0.6rem" }}>
                    <span
                      style={{
                        display: "inline-block",
                        padding: "0.12rem 0.4rem",
                        borderRadius: "3px",
                        fontSize: "0.72rem",
                        fontWeight: 600,
                        border: "1px solid",
                        ...sourceBadgeStyle(entry.source),
                      }}
                    >
                      {entry.source}
                    </span>
                  </td>
                  <td
                    style={{
                      color: entry.level.toUpperCase() === "ERROR" ? "#fca5a5" : "#e6edf3",
                      wordBreak: "break-word",
                      lineHeight: "1.35",
                      padding: "0.35rem 0.6rem",
                    }}
                  >
                    {entry.message}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Bottom Summary Bar */}
      <div style={{ marginTop: "0.5rem" }}>
        <StatusRow label="Info Hash" mono>
          {torrent.infoHash}
        </StatusRow>
        <StatusRow label="Current Status">
          <span className={`badge badge-${torrent.status.toLowerCase()}`}>
            {torrent.status}
          </span>
        </StatusRow>
      </div>
    </div>
  );
}
