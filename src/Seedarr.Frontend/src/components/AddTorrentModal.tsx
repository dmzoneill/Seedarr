import { useState, useRef, useCallback, useEffect } from "react";
import { useNavigate } from "react-router";
import {
  useAddTorrent,
  useIndexers,
  useIndexerSearch,
  useDownloadIndexerRelease,
  AddTorrentResult,
} from "../api/hooks";
import { formatBytes, formatDate } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import type { ReleaseInfo } from "../api/types";

interface AddTorrentModalProps {
  initialMode?: "file" | "magnet" | "search";
  initialQuery?: string;
  onClose: () => void;
}

type InputMode = "file" | "magnet" | "search";

function AddTorrentModal({
  initialMode = "file",
  initialQuery = "",
  onClose,
}: AddTorrentModalProps) {
  const [mode, setMode] = useState<InputMode>(initialMode);
  const [files, setFiles] = useState<File[]>([]);
  const [magnetLink, setMagnetLink] = useState("");
  const [isDragOver, setIsDragOver] = useState(false);
  const [resultMessage, setResultMessage] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const addTorrent = useAddTorrent();
  const { showToast } = useToast();
  const navigate = useNavigate();

  // Indexer Search State
  const [searchQuery, setSearchQuery] = useState(initialQuery);
  const [activeSearchTerm, setActiveSearchTerm] = useState(initialQuery);
  const [selectedIndexerId, setSelectedIndexerId] = useState<
    number | undefined
  >(undefined);
  const [downloadingGuid, setDownloadingGuid] = useState<string | null>(null);

  const { data: indexers } = useIndexers();
  const enabledIndexers = indexers?.filter((i) => i.enable) || [];

  const searchResults = useIndexerSearch(
    {
      query: activeSearchTerm,
      indexerId: selectedIndexerId,
    },
    mode === "search" && Boolean(activeSearchTerm.trim()),
  );

  const downloadReleaseMutation = useDownloadIndexerRelease();

  useEffect(() => {
    if (initialQuery) {
      setSearchQuery(initialQuery);
      setActiveSearchTerm(initialQuery);
    }
  }, [initialQuery]);

  // Debounced auto-search as user types
  useEffect(() => {
    const trimmed = searchQuery.trim();
    if (trimmed !== activeSearchTerm) {
      const timer = setTimeout(() => {
        setActiveSearchTerm(trimmed);
      }, 350);
      return () => clearTimeout(timer);
    }
  }, [searchQuery, activeSearchTerm]);

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const addFiles = useCallback((incoming: FileList | File[]) => {
    const torrentFiles = Array.from(incoming).filter((f) =>
      f.name.endsWith(".torrent"),
    );
    if (torrentFiles.length === 0) return;
    setFiles((prev) => {
      const existing = new Set(prev.map((f) => f.name));
      const merged = [...prev];
      for (const f of torrentFiles) {
        if (!existing.has(f.name)) {
          merged.push(f);
          existing.add(f.name);
        }
      }
      return merged;
    });
  }, []);

  const removeFile = (name: string) => {
    setFiles((prev) => prev.filter((f) => f.name !== name));
  };

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setIsDragOver(false);
      addFiles(e.dataTransfer.files);
    },
    [addFiles],
  );

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      addFiles(e.target.files);
    }
    e.target.value = "";
  };

  const handleSubmit = () => {
    if (mode === "file" && files.length > 0) {
      setResultMessage(null);
      addTorrent.mutate(
        { files },
        {
          onSuccess: (result: AddTorrentResult) => {
            if (result.failed.length === 0) {
              onClose();
              return;
            }
            const failedNames = new Set(result.failed.map((f) => f.fileName));
            setFiles((prev) => prev.filter((f) => failedNames.has(f.name)));
            setResultMessage(
              `${result.added.length} added, ${result.failed.length} skipped: ${result.failed
                .map((f) => `${f.fileName} (${f.reason})`)
                .join("; ")}`,
            );
          },
        },
      );
    } else if (mode === "magnet" && magnetLink.trim()) {
      addTorrent.mutate(
        { magnetLink: magnetLink.trim() },
        { onSuccess: () => onClose() },
      );
    }
  };

  const handleSearchSubmit = (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (searchQuery.trim()) {
      setActiveSearchTerm(searchQuery.trim());
    }
  };

  const handleAddRelease = (release: ReleaseInfo) => {
    const itemKey = release.guid || release.infoHash || release.title;
    setDownloadingGuid(itemKey);

    downloadReleaseMutation.mutate(
      {
        title: release.title,
        downloadUrl: release.downloadUrl || undefined,
        magnetUrl: release.magnetUrl || undefined,
        infoHash: release.infoHash || undefined,
        indexerId: release.indexerId,
        indexerName: release.indexer,
      },
      {
        onSuccess: () => {
          setDownloadingGuid(null);
          showToast(
            `Added "${release.title}" to active seeding library`,
            "success",
          );
        },
        onError: (err) => {
          setDownloadingGuid(null);
          showToast(
            `Failed to add release: ${err.message || "Unknown error"}`,
            "error",
          );
        },
      },
    );
  };

  const canSubmit =
    (mode === "file" && files.length > 0) ||
    (mode === "magnet" && magnetLink.trim().startsWith("magnet:?"));

  return (
    <div className="modal-overlay" onClick={handleBackdropClick}>
      <div
        className="modal"
        style={
          mode === "search" ? { maxWidth: "820px", width: "90%" } : undefined
        }
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
          }}
        >
          <h2 className="modal-title" style={{ margin: 0 }}>
            Add Torrent
          </h2>
          <button
            type="button"
            className="btn btn-outline"
            style={{ padding: "0.2rem 0.5rem", fontSize: "0.85rem" }}
            onClick={onClose}
          >
            ✕
          </button>
        </div>

        <div className="tab-nav">
          <button
            className={`tab-btn ${mode === "file" ? "tab-btn-active" : ""}`}
            onClick={() => setMode("file")}
          >
            Torrent File
          </button>
          <button
            className={`tab-btn ${mode === "magnet" ? "tab-btn-active" : ""}`}
            onClick={() => setMode("magnet")}
          >
            Magnet Link
          </button>
          <button
            className={`tab-btn ${mode === "search" ? "tab-btn-active" : ""}`}
            onClick={() => setMode("search")}
          >
            🔍 Indexer Search
          </button>
        </div>

        {mode === "file" && (
          <>
            <div
              className={`drop-zone ${isDragOver ? "drop-zone-active" : ""} ${files.length > 0 ? "drop-zone-has-file" : ""}`}
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => fileInputRef.current?.click()}
            >
              {files.length > 0 ? (
                <span className="drop-zone-filename">
                  {files.length === 1
                    ? `${files[0].name} selected`
                    : `${files.length} torrents selected`}
                </span>
              ) : (
                <span className="drop-zone-prompt">
                  Drop .torrent files here or click to browse
                </span>
              )}
            </div>
            {files.length > 0 && (
              <ul className="add-torrent-file-list">
                {files.map((f) => (
                  <li key={f.name} className="add-torrent-file-item">
                    <span className="add-torrent-file-name">{f.name}</span>
                    <button
                      type="button"
                      className="add-torrent-file-remove"
                      onClick={() => removeFile(f.name)}
                      disabled={addTorrent.isPending}
                      aria-label={`Remove ${f.name}`}
                    >
                      ×
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <input
              ref={fileInputRef}
              type="file"
              accept=".torrent"
              multiple
              onChange={handleFileChange}
              style={{ display: "none" }}
            />
          </>
        )}

        {mode === "magnet" && (
          <input
            type="text"
            className="search-input modal-magnet-input"
            placeholder="magnet:?xt=urn:btih:..."
            value={magnetLink}
            onChange={(e) => setMagnetLink(e.target.value)}
            autoFocus
          />
        )}

        {mode === "search" && (
          <div
            style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
          >
            {enabledIndexers.length === 0 ? (
              <div
                style={{
                  padding: "2rem 1.5rem",
                  textAlign: "center",
                  backgroundColor: "var(--bg-secondary, #222)",
                  borderRadius: "6px",
                  border: "1px solid var(--border-color, #333)",
                }}
              >
                <div
                  style={{
                    fontSize: "1.05rem",
                    fontWeight: 600,
                    marginBottom: "0.5rem",
                  }}
                >
                  No Indexers Configured
                </div>
                <p
                  style={{
                    color: "var(--text-muted, #888)",
                    fontSize: "0.85rem",
                    maxWidth: "480px",
                    margin: "0 auto 1rem auto",
                  }}
                >
                  To search and download torrent releases directly, configure a{" "}
                  <strong>Prowlarr</strong> or{" "}
                  <strong>Torznab / Newznab</strong> indexer in Settings.
                </p>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => {
                    onClose();
                    navigate("/settings/indexers");
                  }}
                >
                  ⚙️ Configure Indexers
                </button>
              </div>
            ) : (
              <>
                <form
                  onSubmit={handleSearchSubmit}
                  style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}
                >
                  <input
                    type="text"
                    className="search-input"
                    placeholder="Search releases (e.g. Ubuntu, Debian, release name)..."
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    style={{ flex: 1, minWidth: "220px" }}
                    autoFocus
                  />
                  {enabledIndexers.length > 1 && (
                    <select
                      className="form-control"
                      value={selectedIndexerId ?? ""}
                      onChange={(e) =>
                        setSelectedIndexerId(
                          e.target.value ? Number(e.target.value) : undefined,
                        )
                      }
                      style={{
                        backgroundColor: "var(--bg-secondary, #222)",
                        color: "inherit",
                        border: "1px solid var(--border-color, #444)",
                        borderRadius: "4px",
                        padding: "0.4rem 0.6rem",
                      }}
                    >
                      <option value="">
                        All Indexers ({enabledIndexers.length})
                      </option>
                      {enabledIndexers.map((idx) => (
                        <option key={idx.id} value={idx.id}>
                          {idx.name} ({idx.indexerType})
                        </option>
                      ))}
                    </select>
                  )}
                  <button
                    type="submit"
                    className="btn btn-primary"
                    disabled={searchResults.isFetching}
                  >
                    {searchResults.isFetching ? "Searching..." : "Search"}
                  </button>
                </form>

                {/* Results section */}
                <div
                  style={{
                    maxHeight: "360px",
                    overflowY: "auto",
                    border: "1px solid var(--border-color, #333)",
                    borderRadius: "4px",
                    backgroundColor: "var(--bg-primary, #181818)",
                  }}
                >
                  {searchResults.isFetching && (
                    <div style={{ padding: "2rem", textAlign: "center" }}>
                      <div className="loading">
                        Searching configured indexers...
                      </div>
                    </div>
                  )}

                  {searchResults.isError && (
                    <div
                      style={{
                        padding: "1.5rem",
                        color: "var(--danger, #dc3545)",
                        textAlign: "center",
                      }}
                    >
                      Search failed:{" "}
                      {(searchResults.error as Error)?.message ||
                        "Check indexer connection"}
                    </div>
                  )}

                  {!searchResults.isFetching &&
                    !searchResults.isError &&
                    activeSearchTerm &&
                    (searchResults.data?.length ?? 0) === 0 && (
                      <div
                        style={{
                          padding: "2rem",
                          textAlign: "center",
                          color: "var(--text-muted, #888)",
                        }}
                      >
                        No releases found for "{activeSearchTerm}". Try
                        different keywords or indexer.
                      </div>
                    )}

                  {!searchResults.isFetching && !activeSearchTerm && (
                    <div
                      style={{
                        padding: "2rem",
                        textAlign: "center",
                        color: "var(--text-muted, #888)",
                      }}
                    >
                      Type a keyword above to search across your configured
                      indexers ({enabledIndexers.map((i) => i.name).join(", ")}
                      ).
                    </div>
                  )}

                  {!searchResults.isFetching &&
                    (searchResults.data?.length ?? 0) > 0 && (
                      <table
                        className="table"
                        style={{ width: "100%", borderCollapse: "collapse" }}
                      >
                        <thead>
                          <tr
                            style={{
                              borderBottom:
                                "1px solid var(--border-color, #333)",
                              textAlign: "left",
                              fontSize: "0.8rem",
                            }}
                          >
                            <th style={{ padding: "0.6rem 0.8rem" }}>Title</th>
                            <th
                              style={{
                                padding: "0.6rem 0.8rem",
                                width: "110px",
                              }}
                            >
                              Indexer
                            </th>
                            <th
                              style={{
                                padding: "0.6rem 0.8rem",
                                width: "90px",
                              }}
                            >
                              Size
                            </th>
                            <th
                              style={{
                                padding: "0.6rem 0.8rem",
                                width: "80px",
                              }}
                            >
                              Peers
                            </th>
                            <th
                              style={{
                                padding: "0.6rem 0.8rem",
                                width: "90px",
                              }}
                            >
                              Date
                            </th>
                            <th
                              style={{
                                padding: "0.6rem 0.8rem",
                                width: "90px",
                                textAlign: "right",
                              }}
                            >
                              Action
                            </th>
                          </tr>
                        </thead>
                        <tbody>
                          {searchResults.data?.map((rel) => {
                            const itemKey =
                              rel.guid || rel.infoHash || rel.title;
                            const isDownloading = downloadingGuid === itemKey;

                            return (
                              <tr
                                key={itemKey}
                                style={{
                                  borderBottom:
                                    "1px solid var(--border-color, #222)",
                                  fontSize: "0.85rem",
                                }}
                              >
                                <td style={{ padding: "0.6rem 0.8rem" }}>
                                  <div
                                    style={{
                                      fontWeight: 500,
                                      wordBreak: "break-word",
                                    }}
                                  >
                                    {rel.title}
                                  </div>
                                  {rel.categories &&
                                    rel.categories.length > 0 && (
                                      <div
                                        style={{
                                          display: "flex",
                                          gap: "0.3rem",
                                          marginTop: "0.2rem",
                                        }}
                                      >
                                        {rel.categories
                                          .slice(0, 3)
                                          .map((c, i) => (
                                            <span
                                              key={i}
                                              className="badge badge-secondary"
                                              style={{
                                                fontSize: "0.65rem",
                                                padding: "0.1rem 0.3rem",
                                              }}
                                            >
                                              {c}
                                            </span>
                                          ))}
                                      </div>
                                    )}
                                </td>

                                <td style={{ padding: "0.6rem 0.8rem" }}>
                                  <span
                                    className="badge badge-primary"
                                    style={{ fontSize: "0.75rem" }}
                                  >
                                    {rel.indexer || "Indexer"}
                                  </span>
                                </td>

                                <td style={{ padding: "0.6rem 0.8rem" }}>
                                  {formatBytes(rel.size)}
                                </td>

                                <td style={{ padding: "0.6rem 0.8rem" }}>
                                  <span
                                    style={{
                                      color: "var(--success, #28a745)",
                                      fontWeight: 600,
                                    }}
                                  >
                                    ↑{rel.seeders ?? 0}
                                  </span>{" "}
                                  <span
                                    style={{ color: "var(--text-muted, #888)" }}
                                  >
                                    ↓{rel.leechers ?? 0}
                                  </span>
                                </td>

                                <td
                                  style={{
                                    padding: "0.6rem 0.8rem",
                                    fontSize: "0.8rem",
                                    color: "var(--text-muted, #888)",
                                  }}
                                >
                                  {rel.publishDate
                                    ? formatDate(rel.publishDate)
                                    : "-"}
                                </td>

                                <td
                                  style={{
                                    padding: "0.6rem 0.8rem",
                                    textAlign: "right",
                                  }}
                                >
                                  <button
                                    className="btn btn-success"
                                    style={{
                                      fontSize: "0.75rem",
                                      padding: "0.25rem 0.6rem",
                                    }}
                                    onClick={() => handleAddRelease(rel)}
                                    disabled={isDownloading}
                                  >
                                    {isDownloading ? "Adding..." : "+ Add"}
                                  </button>
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    )}
                </div>
              </>
            )}
          </div>
        )}

        {(addTorrent.isError || resultMessage) && (
          <div className="modal-error" style={{ marginTop: "1rem" }}>
            {addTorrent.isError
              ? addTorrent.error instanceof Error
                ? addTorrent.error.message
                : "Failed to add torrent"
              : resultMessage}
          </div>
        )}

        {mode !== "search" && (
          <div className="modal-actions" style={{ marginTop: "1rem" }}>
            <button
              className="btn"
              onClick={onClose}
              disabled={addTorrent.isPending}
            >
              Cancel
            </button>
            <button
              className="btn btn-success"
              onClick={handleSubmit}
              disabled={!canSubmit || addTorrent.isPending}
            >
              {addTorrent.isPending ? "Adding..." : "Add"}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

export default AddTorrentModal;
