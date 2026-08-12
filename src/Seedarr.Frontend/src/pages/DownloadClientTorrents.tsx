import { useState, useMemo } from "react";
import { useParams, Link, useNavigate } from "react-router";
import {
  useDownloadClients,
  useDownloadClientItems,
  useImportDownloadClientTorrent,
  useImportDownloadClientTorrents,
} from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { formatBytes } from "../utils/formatters";

export default function DownloadClientTorrents() {
  const { id } = useParams<{ id: string }>();
  const clientId = parseInt(id || "0", 10);
  const navigate = useNavigate();
  const { showToast } = useToast();

  const { data: clients, isLoading: clientsLoading } = useDownloadClients();
  const client = useMemo(
    () => clients?.find((c) => c.id === clientId),
    [clients, clientId],
  );

  const {
    data: items,
    isLoading: itemsLoading,
    isError,
    error,
    refetch,
    isFetching,
  } = useDownloadClientItems(clientId);

  const importOneMutation = useImportDownloadClientTorrent(clientId);
  const importAllMutation = useImportDownloadClientTorrents(clientId);

  const [searchTerm, setSearchTerm] = useState("");
  const [filterMode, setFilterMode] = useState<"all" | "missing" | "library">(
    "all",
  );
  const [importingHash, setImportingHash] = useState<string | null>(null);

  const totalCount = items?.length || 0;
  const inLibraryCount = items?.filter((i) => i.isInLibrary).length || 0;
  const missingCount = totalCount - inLibraryCount;

  const filteredItems = useMemo(() => {
    if (!items) return [];
    return items.filter((item) => {
      if (filterMode === "missing" && item.isInLibrary) return false;
      if (filterMode === "library" && !item.isInLibrary) return false;
      if (!searchTerm.trim()) return true;

      const term = searchTerm.toLowerCase();
      return (
        item.title.toLowerCase().includes(term) ||
        (item.category && item.category.toLowerCase().includes(term)) ||
        item.infoHash.toLowerCase().includes(term) ||
        (item.outputPath && item.outputPath.toLowerCase().includes(term))
      );
    });
  }, [items, filterMode, searchTerm]);

  const handleImportOne = (hash: string, title: string) => {
    setImportingHash(hash);
    importOneMutation.mutate(hash, {
      onSuccess: () => {
        setImportingHash(null);
        showToast(`Imported "${title}" into Seedarr library`, "success");
      },
      onError: (err) => {
        setImportingHash(null);
        showToast(
          `Failed to import "${title}": ${err.message || "Unknown error"}`,
          "error",
        );
      },
    });
  };

  const handleImportAllMissing = () => {
    if (!items) return;
    const missingHashes = items
      .filter((i) => !i.isInLibrary && i.infoHash)
      .map((i) => i.infoHash);

    if (missingHashes.length === 0) {
      showToast(
        "All torrents from this client are already in the library.",
        "info",
      );
      return;
    }

    importAllMutation.mutate(missingHashes, {
      onSuccess: (res) => {
        showToast(
          `Import Complete: ${res.added} added, ${res.skipped} skipped, ${res.failed} failed.`,
          res.failed > 0 ? "error" : "success",
        );
      },
      onError: (err) => {
        showToast(
          `Bulk import failed: ${err.message || "Unknown error"}`,
          "error",
        );
      },
    });
  };

  if (clientsLoading) {
    return (
      <div className="content-area">
        <div className="card">
          <div className="loading">Loading download client...</div>
        </div>
      </div>
    );
  }

  if (!client) {
    return (
      <div className="content-area">
        <div className="card">
          <div className="empty-state">
            <div className="empty-state-title">Download Client Not Found</div>
            <div className="empty-state-text">
              The requested download client does not exist or has been removed.
            </div>
            <Link
              to="/settings/download-clients"
              className="btn btn-primary"
              style={{ marginTop: "1rem", display: "inline-block" }}
            >
              Go to Download Client Settings
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="content-area">
      {/* Header bar */}
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1rem",
        }}
      >
        <div>
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h2 style={{ margin: 0 }}>{client.name}</h2>
            <span className="badge badge-primary">{client.clientType}</span>
            <span className="badge badge-secondary">
              {client.host}:{client.port}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.85rem",
              color: "var(--text-muted, #888)",
              marginTop: "0.25rem",
            }}
          >
            Live torrent list from {client.clientType} download agent
          </div>
        </div>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          {client?.host && (
            <a
              href={`${client.useSsl ? "https" : "http"}://${client.host}${client.port ? `:${client.port}` : ""}`}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-outline"
              style={{ fontSize: "0.85rem", textDecoration: "none" }}
              title={`Open ${client.name} Web UI`}
            >
              Open Web UI ↗
            </a>
          )}
          <button
            className="btn btn-outline"
            onClick={() => refetch()}
            disabled={isFetching}
          >
            {isFetching ? "Refreshing..." : "↻ Refresh"}
          </button>
          <button
            className="btn btn-primary"
            onClick={handleImportAllMissing}
            disabled={missingCount === 0 || importAllMutation.isPending}
            title={
              missingCount > 0
                ? `Import ${missingCount} missing torrents into library`
                : "All torrents are already in library"
            }
          >
            {importAllMutation.isPending
              ? "Importing..."
              : `Import All Missing (${missingCount})`}
          </button>
        </div>
      </div>

      {/* Stats and filter bar */}
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
          <button
            className={`btn ${filterMode === "all" ? "btn-primary" : "btn-outline"}`}
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={() => setFilterMode("all")}
          >
            All ({totalCount})
          </button>
          <button
            className={`btn ${filterMode === "missing" ? "btn-primary" : "btn-outline"}`}
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={() => setFilterMode("missing")}
          >
            Not in Library ({missingCount})
          </button>
          <button
            className={`btn ${filterMode === "library" ? "btn-primary" : "btn-outline"}`}
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={() => setFilterMode("library")}
          >
            In Library ({inLibraryCount})
          </button>
        </div>

        <div style={{ minWidth: "240px", flex: "1", maxWidth: "350px" }}>
          <input
            type="text"
            className="form-control"
            placeholder="Search by title, category, hash..."
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

      {/* Main content table */}
      <div className="card" style={{ padding: 0, overflow: "hidden" }}>
        {itemsLoading && (
          <div style={{ padding: "2rem", textAlign: "center" }}>
            <div className="loading">
              Connecting to {client.name} and fetching torrents...
            </div>
          </div>
        )}

        {isError && (
          <div style={{ padding: "2rem", textAlign: "center" }}>
            <div
              style={{
                color: "var(--danger, #dc3545)",
                fontWeight: 600,
                marginBottom: "0.5rem",
              }}
            >
              Unable to connect to download client
            </div>
            <div
              style={{
                color: "var(--text-muted, #888)",
                fontSize: "0.9rem",
                marginBottom: "1rem",
              }}
            >
              {(error as Error)?.message || "Connection refused or timed out."}
            </div>
            <Link to="/settings/download-clients" className="btn btn-outline">
              Check Client Configuration
            </Link>
          </div>
        )}

        {!itemsLoading && !isError && filteredItems.length === 0 && (
          <div className="empty-state" style={{ padding: "3rem 1rem" }}>
            <div className="empty-state-title">No Torrents Found</div>
            <div className="empty-state-text">
              {searchTerm || filterMode !== "all"
                ? "No torrents match the active search or filter criteria."
                : `No torrents currently reported by ${client.name}.`}
            </div>
          </div>
        )}

        {!itemsLoading && !isError && filteredItems.length > 0 && (
          <div style={{ overflowX: "auto" }}>
            <table
              className="table"
              style={{ width: "100%", borderCollapse: "collapse" }}
            >
              <thead>
                <tr
                  style={{
                    borderBottom: "1px solid var(--border-color, #333)",
                    textAlign: "left",
                  }}
                >
                  <th style={{ padding: "0.75rem 1rem" }}>Name</th>
                  <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                    Status
                  </th>
                  <th style={{ padding: "0.75rem 1rem", width: "150px" }}>
                    Size & Progress
                  </th>
                  <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                    Category
                  </th>
                  <th style={{ padding: "0.75rem 1rem" }}>Save Path</th>
                  <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                    Library State
                  </th>
                  <th
                    style={{
                      padding: "0.75rem 1rem",
                      width: "130px",
                      textAlign: "right",
                    }}
                  >
                    Action
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredItems.map((item) => {
                  const isImporting =
                    importingHash === item.infoHash ||
                    (importAllMutation.isPending && !item.isInLibrary);

                  return (
                    <tr
                      key={item.infoHash || item.downloadId}
                      style={{
                        borderBottom: "1px solid var(--border-color, #222)",
                        transition: "background-color 0.15s ease",
                      }}
                    >
                      <td style={{ padding: "0.75rem 1rem" }}>
                        <div
                          style={{ fontWeight: 500, wordBreak: "break-word" }}
                        >
                          {item.title}
                        </div>
                        <div
                          style={{
                            fontSize: "0.75rem",
                            color: "var(--text-muted, #777)",
                            fontFamily: "monospace",
                            marginTop: "0.2rem",
                          }}
                        >
                          {item.infoHash}
                        </div>
                      </td>

                      <td style={{ padding: "0.75rem 1rem" }}>
                        <span
                          className={`badge ${
                            item.status?.toLowerCase() === "seeding"
                              ? "badge-success"
                              : item.status?.toLowerCase() === "downloading"
                                ? "badge-primary"
                                : "badge-secondary"
                          }`}
                        >
                          {item.status || "unknown"}
                        </span>
                      </td>

                      <td style={{ padding: "0.75rem 1rem" }}>
                        <div
                          style={{
                            fontSize: "0.85rem",
                            marginBottom: "0.25rem",
                          }}
                        >
                          {formatBytes(item.totalSize)} •{" "}
                          {item.progress.toFixed(1)}%
                        </div>
                        <div
                          style={{
                            height: "5px",
                            backgroundColor: "var(--bg-secondary, #333)",
                            borderRadius: "3px",
                            overflow: "hidden",
                            width: "100%",
                          }}
                        >
                          <div
                            style={{
                              width: `${Math.min(100, Math.max(0, item.progress))}%`,
                              height: "100%",
                              backgroundColor:
                                item.progress >= 100
                                  ? "var(--success, #28a745)"
                                  : "var(--primary, #007bff)",
                            }}
                          />
                        </div>
                      </td>

                      <td style={{ padding: "0.75rem 1rem" }}>
                        {item.category ? (
                          <span className="badge badge-secondary">
                            {item.category}
                          </span>
                        ) : (
                          <span
                            style={{
                              color: "var(--text-muted, #666)",
                              fontSize: "0.85rem",
                            }}
                          >
                            -
                          </span>
                        )}
                      </td>

                      <td
                        style={{
                          padding: "0.75rem 1rem",
                          fontSize: "0.85rem",
                          wordBreak: "break-all",
                        }}
                      >
                        {item.outputPath || "-"}
                      </td>

                      <td style={{ padding: "0.75rem 1rem" }}>
                        {item.isInLibrary ? (
                          <span
                            className="badge badge-success"
                            style={{
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.3rem",
                            }}
                          >
                            ✓ In Library
                          </span>
                        ) : (
                          <span
                            className="badge badge-amber"
                            style={{
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.3rem",
                              backgroundColor: "rgba(255, 193, 7, 0.15)",
                              color: "var(--warning, #ffc107)",
                              border: "1px solid rgba(255, 193, 7, 0.3)",
                            }}
                          >
                            Not in Library
                          </span>
                        )}
                      </td>

                      <td
                        style={{ padding: "0.75rem 1rem", textAlign: "right" }}
                      >
                        {item.isInLibrary ? (
                          <button
                            className="btn btn-outline"
                            style={{
                              fontSize: "0.8rem",
                              padding: "0.3rem 0.6rem",
                            }}
                            onClick={() => {
                              if (item.libraryTorrentId) {
                                navigate(`/torrent/${item.libraryTorrentId}`);
                              } else {
                                navigate("/torrents");
                              }
                            }}
                          >
                            View
                          </button>
                        ) : (
                          <button
                            className="btn btn-success"
                            style={{
                              fontSize: "0.8rem",
                              padding: "0.3rem 0.6rem",
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.3rem",
                            }}
                            onClick={() =>
                              handleImportOne(item.infoHash, item.title)
                            }
                            disabled={isImporting}
                          >
                            {isImporting ? "Adding..." : "+ Add"}
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
      </div>
    </div>
  );
}
