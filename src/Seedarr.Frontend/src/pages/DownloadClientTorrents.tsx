import { useState, useMemo } from "react";
import { useParams, Link, useNavigate } from "react-router";
import {
  useDownloadClients,
  useDownloadClientItems,
  useImportDownloadClientTorrent,
  useImportDownloadClientTorrents,
  useDownloadHistory,
  useArrConnections,
  useBoostHash,
} from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { formatBytes } from "../utils/formatters";
import { getMediaDeepLink } from "../utils/arrLinks";

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

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  const importOneMutation = useImportDownloadClientTorrent(clientId);
  const importAllMutation = useImportDownloadClientTorrents(clientId);
  const boostHashMutation = useBoostHash();

  const handleBoostTorrent = (hash: string, title: string) => {
    boostHashMutation.mutate(
      { infoHash: hash, name: title },
      {
        onSuccess: (res) => {
          showToast(res.message, res.boosted ? "success" : "info");
        },
        onError: (err) => {
          showToast(`Failed to boost swarm: ${err.message}`, "error");
        },
      },
    );
  };

  const [viewMode, setViewMode] = useState<"grid" | "table">("table");
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
        <div className="card" style={{ padding: "3rem", textAlign: "center" }}>
          <div className="loading">Loading download client...</div>
        </div>
      </div>
    );
  }

  if (!client) {
    return (
      <div className="content-area">
        <div className="card" style={{ padding: "3rem", textAlign: "center" }}>
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
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
      }}
    >
      {/* Header Row */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          flexWrap: "wrap",
          gap: "0.75rem",
          flexShrink: 0,
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              {client.name} ({totalCount})
            </h1>
            <span className="badge badge-primary">{client.clientType}</span>
            <span className="badge badge-secondary">
              {client.host}:{client.port}
            </span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Live torrent list from {client.clientType} download agent
          </div>
        </div>

        <div
          className="page-header-actions"
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          {/* View Mode Toggle */}
          <div className="view-toggle">
            <button
              className={`view-toggle-btn ${viewMode === "grid" ? "active" : ""}`}
              onClick={() => setViewMode("grid")}
              title="Poster Card Grid View"
            >
              🎬 Posters
            </button>
            <button
              className={`view-toggle-btn ${viewMode === "table" ? "active" : ""}`}
              onClick={() => setViewMode("table")}
              title="Detailed Table View"
            >
              📋 Table
            </button>
          </div>

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
          marginBottom: "1.25rem",
          padding: "0.75rem 1rem",
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          flexShrink: 0,
        }}
      >
        <div
          style={{
            display: "flex",
            gap: "0.4rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <button
            className={`btn ${filterMode === "all" ? "btn-primary" : "btn-outline"}`}
            style={{
              fontSize: "0.82rem",
              padding: "0.35rem 0.85rem",
              borderRadius: "6px",
              fontWeight: 500,
            }}
            onClick={() => setFilterMode("all")}
          >
            All ({totalCount})
          </button>
          <button
            className={`btn ${filterMode === "missing" ? "btn-primary" : "btn-outline"}`}
            style={{
              fontSize: "0.82rem",
              padding: "0.35rem 0.85rem",
              borderRadius: "6px",
              fontWeight: 500,
            }}
            onClick={() => setFilterMode("missing")}
          >
            Not in Library ({missingCount})
          </button>
          <button
            className={`btn ${filterMode === "library" ? "btn-primary" : "btn-outline"}`}
            style={{
              fontSize: "0.82rem",
              padding: "0.35rem 0.85rem",
              borderRadius: "6px",
              fontWeight: 500,
            }}
            onClick={() => setFilterMode("library")}
          >
            In Library ({inLibraryCount})
          </button>
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            minWidth: "260px",
            flex: "1",
            maxWidth: "450px",
          }}
        >
          <input
            type="text"
            className="form-control"
            placeholder="Filter client torrents by title, category, hash..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            style={{
              width: "100%",
              padding: "0.4rem 0.75rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary)",
              color: "inherit",
              fontSize: "0.85rem",
            }}
          />
          {searchTerm && (
            <button
              className="btn btn-outline"
              onClick={() => setSearchTerm("")}
              style={{
                fontSize: "0.75rem",
                padding: "0.35rem 0.5rem",
                borderRadius: "6px",
              }}
              title="Clear search filter"
            >
              ✕
            </button>
          )}
        </div>
      </div>

      {/* Main Content Area */}
      {itemsLoading && (
        <div
          className="card"
          style={{ padding: "3rem", textAlign: "center", borderRadius: "8px" }}
        >
          <div className="loading">
            Connecting to {client.name} and fetching torrents...
          </div>
        </div>
      )}

      {isError && (
        <div
          className="card"
          style={{
            padding: "2.5rem 1.5rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            style={{
              color: "var(--danger)",
              fontWeight: 600,
              fontSize: "1.1rem",
              marginBottom: "0.5rem",
            }}
          >
            Unable to connect to download client
          </div>
          <div
            style={{
              color: "var(--text-muted)",
              fontSize: "0.9rem",
              marginBottom: "1.25rem",
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
        <div
          className="card empty-state"
          style={{
            padding: "3.5rem 1rem",
            textAlign: "center",
            borderRadius: "8px",
          }}
        >
          <div
            className="empty-state-title"
            style={{
              fontSize: "1.25rem",
              fontWeight: 600,
              marginBottom: "0.5rem",
            }}
          >
            No Torrents Found
          </div>
          <div
            className="empty-state-text"
            style={{
              color: "var(--text-muted)",
              maxWidth: "500px",
              margin: "0 auto",
            }}
          >
            {searchTerm || filterMode !== "all"
              ? "No torrents match the active search or filter criteria."
              : `No torrents currently reported by ${client.name}.`}
          </div>
        </div>
      )}

      {/* POSTER GRID VIEW */}
      {!itemsLoading &&
        !isError &&
        filteredItems.length > 0 &&
        viewMode === "grid" && (
          <div
            style={{
              flex: "1 1 auto",
              minHeight: 0,
              overflowY: "auto",
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))",
              alignContent: "start",
              gap: "1.25rem",
              paddingRight: "0.25rem",
              paddingBottom: "1rem",
            }}
          >
            {filteredItems.map((item) => {
              const match = history?.find(
                (h) =>
                  (item.infoHash &&
                    h.infoHash?.toLowerCase() ===
                      item.infoHash.toLowerCase()) ||
                  h.title?.toLowerCase() === item.title.toLowerCase(),
              );
              const meta = match?.metadata;
              const displayTitle = meta?.title || item.title;
              const hasPoster = Boolean(meta?.posterUrl);
              const arrLink = match
                ? getMediaDeepLink(match, arrConnections)
                : null;
              const isImporting =
                importingHash === item.infoHash ||
                (importAllMutation.isPending && !item.isInLibrary);

              return (
                <div
                  key={item.infoHash || item.downloadId}
                  className="card"
                  style={{
                    padding: 0,
                    overflow: "hidden",
                    display: "flex",
                    flexDirection: "column",
                    height: "auto",
                    borderRadius: "8px",
                    border: "1px solid rgba(255, 255, 255, 0.08)",
                    backgroundColor: "var(--bg-secondary)",
                    boxShadow:
                      "0 4px 14px rgba(0, 0, 0, 0.35), 0 1px 3px rgba(0, 0, 0, 0.2)",
                    transition: "transform 0.18s ease, box-shadow 0.18s ease",
                  }}
                >
                  {/* Poster Artwork Box */}
                  <div
                    style={{
                      position: "relative",
                      width: "100%",
                      paddingTop: "140%", // 2:3 aspect ratio matching TrackerServer
                      backgroundColor: "#141414",
                      overflow: "hidden",
                    }}
                  >
                    {hasPoster ? (
                      <img
                        src={meta?.posterUrl || ""}
                        alt={displayTitle}
                        style={{
                          position: "absolute",
                          top: 0,
                          left: 0,
                          width: "100%",
                          height: "100%",
                          objectFit: "cover",
                        }}
                        loading="lazy"
                      />
                    ) : (
                      <div
                        style={{
                          position: "absolute",
                          top: 0,
                          left: 0,
                          width: "100%",
                          height: "100%",
                          display: "flex",
                          flexDirection: "column",
                          alignItems: "center",
                          justifyContent: "center",
                          color: "var(--text-muted)",
                          padding: "1rem",
                          textAlign: "center",
                        }}
                      >
                        <span
                          style={{ fontSize: "2.8rem", marginBottom: "0.5rem" }}
                        >
                          ⚡
                        </span>
                        <span
                          style={{
                            fontSize: "0.8rem",
                            wordBreak: "break-word",
                          }}
                        >
                          {item.title}
                        </span>
                      </div>
                    )}

                    {/* Top Badges */}
                    <div
                      style={{
                        position: "absolute",
                        top: "8px",
                        left: "8px",
                        right: "8px",
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                      }}
                    >
                      {arrLink ? (
                        <a
                          href={arrLink.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="badge badge-primary"
                          style={{
                            fontSize: "0.7rem",
                            fontWeight: 600,
                            backgroundColor: "rgba(0, 0, 0, 0.75)",
                            backdropFilter: "blur(4px)",
                            borderRadius: "4px",
                            textDecoration: "none",
                            color: "inherit",
                          }}
                          title={arrLink.label}
                        >
                          {arrLink.appName} ↗
                        </a>
                      ) : item.category ? (
                        <span
                          className="badge badge-primary"
                          style={{
                            fontSize: "0.7rem",
                            fontWeight: 600,
                            backgroundColor: "rgba(0, 0, 0, 0.75)",
                            backdropFilter: "blur(4px)",
                            borderRadius: "4px",
                          }}
                        >
                          {item.category}
                        </span>
                      ) : (
                        <span />
                      )}

                      {item.isInLibrary ? (
                        <span
                          className="badge badge-success"
                          style={{
                            fontSize: "0.7rem",
                            backgroundColor: "rgba(39, 174, 96, 0.85)",
                            backdropFilter: "blur(4px)",
                            borderRadius: "4px",
                          }}
                        >
                          ✓ In Library
                        </span>
                      ) : (
                        <span
                          className="badge badge-warning"
                          style={{
                            fontSize: "0.7rem",
                            backgroundColor: "rgba(230, 126, 34, 0.85)",
                            backdropFilter: "blur(4px)",
                            borderRadius: "4px",
                          }}
                        >
                          Not In Library
                        </span>
                      )}
                    </div>

                    {/* Bottom Stats Overlay */}
                    <div
                      style={{
                        position: "absolute",
                        bottom: 0,
                        left: 0,
                        right: 0,
                        padding: "0.4rem 0.6rem",
                        backgroundColor: "rgba(0, 0, 0, 0.82)",
                        backdropFilter: "blur(4px)",
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        fontSize: "0.75rem",
                        color: "#ccc",
                      }}
                    >
                      <span>{formatBytes(item.totalSize)}</span>
                      <span style={{ fontWeight: 600, color: "var(--accent)" }}>
                        {item.progress.toFixed(1)}%
                      </span>
                    </div>
                  </div>

                  {/* Card Details */}
                  <div
                    style={{
                      padding: "0.85rem",
                      display: "flex",
                      flexDirection: "column",
                      flex: 1,
                      gap: "0.4rem",
                    }}
                  >
                    <div
                      style={{
                        fontWeight: 600,
                        fontSize: "0.9rem",
                        lineHeight: "1.25",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        display: "-webkit-box",
                        WebkitLineClamp: 2,
                        WebkitBoxOrient: "vertical",
                      }}
                      title={displayTitle}
                    >
                      {displayTitle} {meta?.year ? `(${meta.year})` : ""}
                    </div>

                    <div
                      style={{
                        fontSize: "0.72rem",
                        color: "var(--text-muted)",
                        fontFamily: "monospace",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {item.infoHash}
                    </div>

                    {/* Action row */}
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        marginTop: "auto",
                        paddingTop: "0.5rem",
                      }}
                    >
                      <span
                        className={`badge ${
                          item.status?.toLowerCase() === "seeding"
                            ? "badge-success"
                            : item.status?.toLowerCase() === "downloading"
                              ? "badge-primary"
                              : "badge-secondary"
                        }`}
                        style={{ fontSize: "0.72rem" }}
                      >
                        {item.status || "unknown"}
                      </span>

                      <div style={{ display: "flex", gap: "0.35rem" }}>
                        <button
                          className="btn btn-primary btn-small"
                          style={{
                            fontSize: "0.78rem",
                            padding: "0.2rem 0.55rem",
                            borderRadius: "4px",
                          }}
                          onClick={() =>
                            handleBoostTorrent(item.infoHash, item.title)
                          }
                          disabled={boostHashMutation.isPending}
                          title="Query candidate trackers via BEP 15/48 scrape and inject verified seeders"
                        >
                          ⚡ Boost
                        </button>

                        {item.isInLibrary ? (
                          <button
                            className="btn btn-outline btn-small"
                            style={{
                              fontSize: "0.78rem",
                              padding: "0.2rem 0.55rem",
                              borderRadius: "4px",
                            }}
                            onClick={() => {
                              if (item.libraryTorrentId) {
                                navigate(`/torrents/${item.libraryTorrentId}`);
                              } else {
                                navigate("/torrents");
                              }
                            }}
                          >
                            View ↗
                          </button>
                        ) : (
                          <button
                            className="btn btn-success btn-small"
                            style={{
                              fontSize: "0.78rem",
                              padding: "0.2rem 0.55rem",
                              borderRadius: "4px",
                            }}
                            onClick={() =>
                              handleImportOne(item.infoHash, item.title)
                            }
                            disabled={isImporting}
                          >
                            {isImporting ? "Importing..." : "+ Import"}
                          </button>
                        )}
                      </div>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}

      {/* DETAILED TABLE VIEW */}
      {!itemsLoading &&
        !isError &&
        filteredItems.length > 0 &&
        viewMode === "table" && (
          <div
            className="card"
            style={{
              padding: 0,
              overflow: "hidden",
              borderRadius: "8px",
              boxShadow:
                "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
              flex: "1 1 auto",
              minHeight: 0,
              display: "flex",
              flexDirection: "column",
            }}
          >
            <div
              style={{
                flex: "1 1 auto",
                minHeight: 0,
                overflowY: "auto",
                overflowX: "auto",
              }}
            >
              <table
                className="table"
                style={{ width: "100%", borderCollapse: "collapse" }}
              >
                <thead
                  style={{
                    position: "sticky",
                    top: 0,
                    zIndex: 2,
                    backgroundColor: "var(--bg-secondary)",
                  }}
                >
                  <tr
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      textAlign: "left",
                      color: "var(--text-muted)",
                      fontSize: "0.8rem",
                    }}
                  >
                    <th style={{ padding: "0.75rem 1rem" }}>Media & Torrent</th>
                    <th style={{ padding: "0.75rem 1rem", width: "130px" }}>
                      Status
                    </th>
                    <th style={{ padding: "0.75rem 1rem", width: "170px" }}>
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
                        width: "120px",
                        textAlign: "right",
                      }}
                    >
                      Action
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {filteredItems.map((item) => {
                    const match = history?.find(
                      (h) =>
                        (item.infoHash &&
                          h.infoHash?.toLowerCase() ===
                            item.infoHash.toLowerCase()) ||
                        h.title?.toLowerCase() === item.title.toLowerCase(),
                    );
                    const meta = match?.metadata;
                    const displayTitle = meta?.title || item.title;
                    const hasPoster = Boolean(meta?.posterUrl);
                    const arrLink = match
                      ? getMediaDeepLink(match, arrConnections)
                      : null;
                    const isImporting =
                      importingHash === item.infoHash ||
                      (importAllMutation.isPending && !item.isInLibrary);

                    return (
                      <tr
                        key={item.infoHash || item.downloadId}
                        style={{
                          borderBottom: "1px solid rgba(255, 255, 255, 0.05)",
                          transition: "background-color 0.15s ease",
                        }}
                      >
                        <td style={{ padding: "0.75rem 1rem" }}>
                          <div
                            style={{
                              display: "flex",
                              alignItems: "center",
                              gap: "0.75rem",
                            }}
                          >
                            {hasPoster ? (
                              <img
                                src={meta?.posterUrl || ""}
                                alt=""
                                style={{
                                  width: "32px",
                                  height: "46px",
                                  objectFit: "cover",
                                  borderRadius: "4px",
                                  border: "1px solid rgba(255, 255, 255, 0.1)",
                                  flexShrink: 0,
                                }}
                              />
                            ) : (
                              <span
                                style={{ fontSize: "1.4rem", flexShrink: 0 }}
                              >
                                ⚡
                              </span>
                            )}

                            <div style={{ minWidth: 0, flex: 1 }}>
                              <div
                                style={{ fontWeight: 500, fontSize: "0.88rem" }}
                              >
                                {displayTitle}{" "}
                                {meta?.year ? `(${meta.year})` : ""}
                              </div>
                              <div
                                style={{
                                  fontSize: "0.72rem",
                                  color: "var(--text-muted)",
                                  fontFamily: "monospace",
                                  marginTop: "0.15rem",
                                  overflow: "hidden",
                                  textOverflow: "ellipsis",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                {item.infoHash}
                              </div>
                            </div>
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
                            style={{ borderRadius: "4px" }}
                          >
                            {item.status || "unknown"}
                          </span>
                        </td>

                        <td style={{ padding: "0.75rem 1rem" }}>
                          <div
                            style={{
                              fontSize: "0.82rem",
                              marginBottom: "0.3rem",
                              display: "flex",
                              justifyContent: "space-between",
                            }}
                          >
                            <span>{formatBytes(item.totalSize)}</span>
                            <span
                              style={{
                                fontWeight: 600,
                                color: "var(--accent)",
                              }}
                            >
                              {item.progress.toFixed(1)}%
                            </span>
                          </div>
                          <div
                            style={{
                              height: "6px",
                              backgroundColor: "var(--bg-primary)",
                              borderRadius: "3px",
                              overflow: "hidden",
                              width: "100%",
                              border: "1px solid rgba(255, 255, 255, 0.05)",
                            }}
                          >
                            <div
                              style={{
                                width: `${Math.min(100, Math.max(0, item.progress))}%`,
                                height: "100%",
                                backgroundColor:
                                  item.progress >= 100
                                    ? "var(--success)"
                                    : "var(--accent)",
                                borderRadius: "3px",
                                transition: "width 0.3s ease",
                              }}
                            />
                          </div>
                        </td>

                        <td style={{ padding: "0.75rem 1rem" }}>
                          <div
                            style={{
                              display: "flex",
                              gap: "0.3rem",
                              alignItems: "center",
                            }}
                          >
                            {item.category && (
                              <span
                                className="badge badge-secondary"
                                style={{ borderRadius: "4px" }}
                              >
                                {item.category}
                              </span>
                            )}
                            {arrLink && (
                              <a
                                href={arrLink.url}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="badge badge-primary"
                                style={{
                                  fontSize: "0.7rem",
                                  padding: "0.15rem 0.4rem",
                                  textDecoration: "none",
                                  borderRadius: "4px",
                                }}
                                title={arrLink.label}
                              >
                                {arrLink.appName} ↗
                              </a>
                            )}
                            {!item.category && !arrLink && (
                              <span
                                style={{
                                  color: "var(--text-muted)",
                                  fontSize: "0.85rem",
                                }}
                              >
                                -
                              </span>
                            )}
                          </div>
                        </td>

                        <td
                          style={{
                            padding: "0.75rem 1rem",
                            fontSize: "0.82rem",
                            color: "var(--text-muted)",
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
                                borderRadius: "4px",
                              }}
                            >
                              ✓ In Library
                            </span>
                          ) : (
                            <span
                              className="badge badge-warning"
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.3rem",
                                borderRadius: "4px",
                              }}
                            >
                              Not in Library
                            </span>
                          )}
                        </td>

                        <td
                          style={{
                            padding: "0.75rem 1rem",
                            textAlign: "right",
                            whiteSpace: "nowrap",
                          }}
                        >
                          <div
                            style={{
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.45rem",
                              whiteSpace: "nowrap",
                            }}
                          >
                            <button
                              className="btn btn-primary"
                              style={{
                                fontSize: "0.78rem",
                                padding: "0.3rem 0.65rem",
                                borderRadius: "4px",
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.35rem",
                                whiteSpace: "nowrap",
                              }}
                              onClick={() =>
                                handleBoostTorrent(item.infoHash, item.title)
                              }
                              disabled={boostHashMutation.isPending}
                              title="Query candidate trackers via BEP 15/48 scrape and inject verified seeders"
                            >
                              <span>⚡</span>
                              <span>Boost</span>
                            </button>

                            {item.isInLibrary ? (
                              <button
                                className="btn btn-outline"
                                style={{
                                  fontSize: "0.78rem",
                                  padding: "0.3rem 0.65rem",
                                  borderRadius: "4px",
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.35rem",
                                  whiteSpace: "nowrap",
                                }}
                                onClick={() => {
                                  if (item.libraryTorrentId) {
                                    navigate(
                                      `/torrents/${item.libraryTorrentId}`,
                                    );
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
                                  fontSize: "0.78rem",
                                  padding: "0.3rem 0.65rem",
                                  borderRadius: "4px",
                                  display: "inline-flex",
                                  alignItems: "center",
                                  gap: "0.35rem",
                                  whiteSpace: "nowrap",
                                }}
                                onClick={() =>
                                  handleImportOne(item.infoHash, item.title)
                                }
                                disabled={isImporting}
                              >
                                {isImporting ? (
                                  <span>Adding...</span>
                                ) : (
                                  <>
                                    <span>+</span>
                                    <span>Add</span>
                                  </>
                                )}
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}
    </div>
  );
}
