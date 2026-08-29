import { useState } from "react";
import {
  useDownloadHistory,
  useReAddHistoryTorrent,
  useDeleteHistoryTorrent,
  useClearDownloadHistory,
} from "../api/hooks";
import { formatBytes, formatRatio, formatDate } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import AddTorrentModal from "../components/AddTorrentModal";

function formatDuration(seconds: number): string {
  if (!seconds || seconds <= 0) return "0s";
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);

  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

export default function DownloadHistory() {
  const [searchTerm, setSearchTerm] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [searchModalQuery, setSearchModalQuery] = useState<string | null>(null);
  const { showToast } = useToast();

  const {
    data: history,
    isLoading,
    isError,
  } = useDownloadHistory({
    query: searchTerm.trim() || undefined,
    status: statusFilter !== "all" ? statusFilter : undefined,
  });

  const reAddMutation = useReAddHistoryTorrent();
  const deleteMutation = useDeleteHistoryTorrent();
  const clearMutation = useClearDownloadHistory();

  const handleReAdd = (id: number, title: string) => {
    reAddMutation.mutate(id, {
      onSuccess: () => {
        showToast(`Re-added "${title}" to active seeding library`, "success");
      },
      onError: (err) => {
        showToast(
          `Failed to re-add "${title}": ${err.message || "Unknown error"}`,
          "error",
        );
      },
    });
  };

  const handleDelete = (id: number, title: string) => {
    if (!confirm(`Delete history record for "${title}"?`)) return;
    deleteMutation.mutate(id, {
      onSuccess: () => {
        showToast("Historical record removed", "info");
      },
      onError: (err) => {
        showToast(`Failed to delete record: ${err.message}`, "error");
      },
    });
  };

  const handleClearAll = () => {
    if (
      !confirm(
        "Are you sure you want to clear all download history? This action cannot be undone.",
      )
    ) {
      return;
    }
    clearMutation.mutate(undefined, {
      onSuccess: () => {
        showToast("Download history cleared successfully", "success");
      },
      onError: (err) => {
        showToast(`Failed to clear history: ${err.message}`, "error");
      },
    });
  };

  const totalCount = history?.length || 0;

  return (
    <div className="content-area">
      <div className="page-heading-row" style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
        <h1 className="page-heading" style={{ margin: 0 }}>Historical Downloads</h1>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button
            className="btn btn-outline"
            onClick={handleClearAll}
            disabled={clearMutation.isPending || totalCount === 0}
            title="Clear all history entries"
          >
            Clear History
          </button>
        </div>
      </div>

      {/* Filter and search toolbar */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1rem",
          padding: "0.75rem 1rem",
        }}
      >
        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          {(["all", "Active", "Completed", "Removed"] as const).map((st) => (
            <button
              key={st}
              className={`btn ${statusFilter === st ? "btn-primary" : "btn-outline"}`}
              style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
              onClick={() => setStatusFilter(st)}
            >
              {st === "all" ? "All" : st}
            </button>
          ))}
        </div>

        <div style={{ minWidth: "240px", flex: "1", maxWidth: "350px" }}>
          <input
            type="text"
            className="form-control"
            placeholder="Search by title, infohash, tracker..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            style={{
              width: "100%",
              padding: "0.4rem 0.75rem",
              borderRadius: "4px",
              border: "1px solid var(--border-color, #444)",
              backgroundColor: "var(--bg-secondary, #222)",
              color: "inherit",
              fontSize: "0.85rem",
            }}
          />
        </div>
      </div>

      {/* History table */}
      <div className="card" style={{ padding: 0, overflow: "hidden" }}>
        {isLoading && (
          <div style={{ padding: "2rem", textAlign: "center" }}>
            <div className="loading">Loading download history...</div>
          </div>
        )}

        {isError && (
          <div style={{ padding: "2rem", textAlign: "center", color: "var(--danger, #dc3545)" }}>
            Failed to load download history.
          </div>
        )}

        {!isLoading && !isError && totalCount === 0 && (
          <div className="empty-state" style={{ padding: "3rem 1rem", textAlign: "center" }}>
            <div className="empty-state-title" style={{ fontSize: "1.2rem", fontWeight: 600, marginBottom: "0.5rem" }}>
              No Historical Downloads
            </div>
            <div className="empty-state-text" style={{ color: "var(--text-muted, #888)" }}>
              {searchTerm || statusFilter !== "all"
                ? "No history records match the current filters."
                : "Downloads and seeded torrents will be permanently recorded here even after removal."}
            </div>
          </div>
        )}

        {!isLoading && !isError && totalCount > 0 && (
          <div style={{ overflowX: "auto" }}>
            <table className="table" style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr style={{ borderBottom: "1px solid var(--border-color, #333)", textAlign: "left" }}>
                  <th style={{ padding: "0.75rem 1rem" }}>Release</th>
                  <th style={{ padding: "0.75rem 1rem", width: "110px" }}>Size</th>
                  <th style={{ padding: "0.75rem 1rem", width: "130px" }}>Uploaded</th>
                  <th style={{ padding: "0.75rem 1rem", width: "90px" }}>Ratio</th>
                  <th style={{ padding: "0.75rem 1rem", width: "110px" }}>Seed Time</th>
                  <th style={{ padding: "0.75rem 1rem", width: "130px" }}>Date Added</th>
                  <th style={{ padding: "0.75rem 1rem", width: "110px" }}>Status</th>
                  <th style={{ padding: "0.75rem 1rem", width: "180px", textAlign: "right" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {history?.map((item) => (
                  <tr
                    key={item.id}
                    style={{
                      borderBottom: "1px solid var(--border-color, #222)",
                      transition: "background-color 0.15s ease",
                    }}
                  >
                    <td style={{ padding: "0.75rem 1rem" }}>
                      <div style={{ fontWeight: 500, wordBreak: "break-word" }}>
                        {item.title}
                      </div>
                      <div
                        style={{
                          fontSize: "0.75rem",
                          color: "var(--text-muted, #777)",
                          fontFamily: "monospace",
                          marginTop: "0.2rem",
                          display: "flex",
                          gap: "0.5rem",
                          alignItems: "center",
                        }}
                      >
                        <span>{item.infoHash}</span>
                        {item.source && (
                          <span className="badge badge-secondary" style={{ fontSize: "0.7rem", padding: "0.1rem 0.4rem" }}>
                            {item.source}
                          </span>
                        )}
                        {item.primaryTracker && (
                          <span style={{ color: "var(--text-dim, #999)" }}>
                            • {item.primaryTracker}
                          </span>
                        )}
                      </div>
                    </td>

                    <td style={{ padding: "0.75rem 1rem", fontSize: "0.85rem" }}>
                      {formatBytes(item.totalSize)}
                    </td>

                    <td style={{ padding: "0.75rem 1rem", fontSize: "0.85rem" }}>
                      {formatBytes(item.uploaded)}
                    </td>

                    <td style={{ padding: "0.75rem 1rem" }}>
                      <span
                        className={`badge ${
                          item.ratio >= 1.0 ? "badge-success" : "badge-secondary"
                        }`}
                        style={{ fontSize: "0.8rem" }}
                      >
                        {formatRatio(item.ratio)}
                      </span>
                    </td>

                    <td style={{ padding: "0.75rem 1rem", fontSize: "0.85rem" }}>
                      {formatDuration(item.seedingTime)}
                    </td>

                    <td style={{ padding: "0.75rem 1rem", fontSize: "0.85rem" }}>
                      <div>{formatDate(item.dateAdded)}</div>
                      {item.dateRemoved && (
                        <div style={{ fontSize: "0.75rem", color: "var(--text-muted, #777)" }}>
                          Removed {formatDate(item.dateRemoved)}
                        </div>
                      )}
                    </td>

                    <td style={{ padding: "0.75rem 1rem" }}>
                      <span
                        className={`badge ${
                          item.status === "Active"
                            ? "badge-success"
                            : item.status === "Completed"
                            ? "badge-primary"
                            : "badge-stopped"
                        }`}
                      >
                        {item.status}
                      </span>
                    </td>

                    <td style={{ padding: "0.75rem 1rem", textAlign: "right" }}>
                      <div style={{ display: "inline-flex", gap: "0.35rem" }}>
                        <button
                          className="btn btn-outline"
                          style={{ fontSize: "0.8rem", padding: "0.25rem 0.5rem" }}
                          onClick={() => setSearchModalQuery(item.title)}
                          title="Search for this release again on Prowlarr"
                        >
                          🔍 Search
                        </button>
                        <button
                          className="btn btn-primary"
                          style={{ fontSize: "0.8rem", padding: "0.25rem 0.5rem" }}
                          onClick={() => handleReAdd(item.id, item.title)}
                          disabled={reAddMutation.isPending || item.status === "Active"}
                          title={item.status === "Active" ? "Already in library" : "Re-add to active seeding library"}
                        >
                          🔄 Re-add
                        </button>
                        <button
                          className="btn btn-outline"
                          style={{ fontSize: "0.8rem", padding: "0.25rem 0.5rem", color: "var(--danger, #dc3545)" }}
                          onClick={() => handleDelete(item.id, item.title)}
                          title="Delete historical record"
                        >
                          ✕
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {searchModalQuery && (
        <AddTorrentModal
          initialMode="search"
          initialQuery={searchModalQuery}
          onClose={() => setSearchModalQuery(null)}
        />
      )}
    </div>
  );
}
