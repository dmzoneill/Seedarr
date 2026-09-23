import { useState } from "react";
import { useLogFiles, useClearLogFiles, useSystemStatus } from "../api/hooks";
import { apiClient } from "../api/client";
import { useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";

function DownloadIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="7 10 12 15 17 10" />
      <line x1="12" y1="15" x2="12" y2="3" />
    </svg>
  );
}

function RefreshIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="23 4 23 10 17 10" />
      <polyline points="1 20 1 14 7 14" />
      <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
    </svg>
  );
}

function TrashIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </svg>
  );
}

function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  const value = bytes / Math.pow(1024, i);
  return `${value.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

function formatRelativeTime(iso: string): string {
  const date = new Date(iso);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHour = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHour / 24);

  if (diffDay > 0) return `${diffDay} day${diffDay > 1 ? "s" : ""} ago`;
  if (diffHour > 0) return `${diffHour} hour${diffHour > 1 ? "s" : ""} ago`;
  if (diffMin > 0) return `${diffMin} minute${diffMin > 1 ? "s" : ""} ago`;
  return "just now";
}

function SystemLogFiles() {
  const { data: logFiles, isLoading, error } = useLogFiles();
  const { data: status } = useSystemStatus();
  const clearLogFiles = useClearLogFiles();
  const queryClient = useQueryClient();
  const [confirmClear, setConfirmClear] = useState(false);
  const [downloadingFile, setDownloadingFile] = useState<string | null>(null);

  const logPath = status?.appDataPath
    ? `${status.appDataPath}/logs`
    : "{appData}/logs";

  const handleRefresh = () => {
    queryClient.invalidateQueries({ queryKey: ["logfiles"] });
  };

  const handleClear = () => {
    setConfirmClear(true);
  };

  const handleConfirmClear = () => {
    clearLogFiles.mutate(undefined, {
      onSettled: () => setConfirmClear(false),
    });
  };

  const handleDownload = async (filename: string) => {
    try {
      setDownloadingFile(filename);
      const res = await apiClient.get<Blob | { data: Blob }>(
        `/log/file/${encodeURIComponent(filename)}`,
        { responseType: "blob" },
      );
      const blob =
        res && typeof res === "object" && "data" in res
          ? (res as { data: Blob }).data
          : (res as Blob);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error("Failed to download log file", err);
    } finally {
      setDownloadingFile(null);
    }
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
            <span>📁</span> System: Log Files
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Rotating plain text log files stored on disk for offline debugging
            and diagnostic exports
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          <button
            className="btn btn-outline btn-small"
            onClick={handleRefresh}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            <RefreshIcon />
            <span>Refresh</span>
          </button>
          <button
            className="btn btn-danger btn-small"
            onClick={handleClear}
            disabled={clearLogFiles.isPending}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            <TrashIcon />
            <span>
              {clearLogFiles.isPending ? "Clearing..." : "Clear Logs"}
            </span>
          </button>
        </div>
      </div>

      {/* Info Alert Box */}
      <div
        className="card"
        style={{
          display: "flex",
          alignItems: "flex-start",
          gap: "0.75rem",
          padding: "0.85rem 1.15rem",
          marginBottom: "1.25rem",
          borderRadius: "8px",
          backgroundColor: "rgba(200, 168, 78, 0.1)",
          border: "1px solid rgba(200, 168, 78, 0.3)",
          fontSize: "0.85rem",
          color: "var(--text-secondary)",
          lineHeight: 1.5,
        }}
      >
        <span
          style={{
            color: "var(--accent, #c8a84e)",
            fontSize: "1.1rem",
            lineHeight: 1,
          }}
        >
          ℹ️
        </span>
        <div>
          Log files are stored at:{" "}
          <code
            style={{
              fontFamily: "monospace",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
            }}
          >
            {logPath}
          </code>
          . You can adjust the logging verbosity level in{" "}
          <Link
            to="/settings/advanced"
            style={{
              color: "var(--accent, #c8a84e)",
              textDecoration: "underline",
            }}
          >
            Settings &gt; Advanced
          </Link>
          .
        </div>
      </div>

      {isLoading && <p className="loading">Loading log files...</p>}

      {error && (
        <div className="card" style={{ marginBottom: "1rem" }}>
          <p className="error">Failed to load log files.</p>
        </div>
      )}

      {/* Log Files Table Card */}
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
        {logFiles && (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Log Filename</th>
                  <th className="torrent-table-th">Last Modified</th>
                  <th className="torrent-table-th">File Size</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Download
                  </th>
                </tr>
              </thead>
              <tbody>
                {logFiles.length === 0 && (
                  <tr>
                    <td colSpan={4} className="torrent-table-empty">
                      No log files currently present on disk.
                    </td>
                  </tr>
                )}
                {logFiles.map((file) => (
                  <tr key={file.filename} className="torrent-table-row">
                    <td>
                      <code
                        style={{
                          fontSize: "0.85rem",
                          color: "var(--text-primary)",
                          fontWeight: 600,
                        }}
                      >
                        {file.filename}
                      </code>
                    </td>
                    <td>{formatRelativeTime(file.lastWriteTime)}</td>
                    <td>{formatFileSize(file.size)}</td>
                    <td style={{ textAlign: "right" }}>
                      <button
                        type="button"
                        className="btn btn-outline btn-small"
                        onClick={() => handleDownload(file.filename)}
                        disabled={downloadingFile === file.filename}
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.4rem",
                        }}
                      >
                        <DownloadIcon />
                        <span>
                          {downloadingFile === file.filename
                            ? "Downloading..."
                            : "Download"}
                        </span>
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {confirmClear && (
        <div className="modal-overlay" onClick={() => setConfirmClear(false)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 460,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <h2 style={{ margin: "0 0 0.75rem", fontSize: "1.2rem" }}>
              Clear Log Files
            </h2>
            <p
              style={{
                margin: "0 0 1.25rem",
                color: "var(--text-secondary)",
                fontSize: "0.9rem",
                lineHeight: 1.4,
              }}
            >
              Are you sure you want to clear log files? All non-active log files
              will be permanently deleted from disk.
            </p>
            <div
              style={{
                display: "flex",
                gap: "0.5rem",
                justifyContent: "flex-end",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setConfirmClear(false)}
                disabled={clearLogFiles.isPending}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-danger btn-small"
                onClick={handleConfirmClear}
                disabled={clearLogFiles.isPending}
              >
                {clearLogFiles.isPending ? "Clearing..." : "Clear Logs"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default SystemLogFiles;
