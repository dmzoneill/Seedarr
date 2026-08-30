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

export interface AddTorrentFormProps {
  initialMode?: "file" | "magnet" | "search";
  initialQuery?: string;
  isModal?: boolean;
  onClose?: () => void;
  onSuccess?: () => void;
}

export type InputMode = "file" | "magnet" | "search";

export function AddTorrentForm({
  initialMode = "file",
  initialQuery = "",
  isModal = false,
  onClose,
  onSuccess,
}: AddTorrentFormProps) {
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
              showToast(`Added ${result.added.length} torrent(s)`, "success");
              if (onSuccess) onSuccess();
              if (onClose) onClose();
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
        {
          onSuccess: () => {
            showToast("Magnet link added successfully", "success");
            setMagnetLink("");
            if (onSuccess) onSuccess();
            if (onClose) onClose();
          },
        },
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

  const isMagnetValid = magnetLink.trim().startsWith("magnet:?");
  const canSubmit =
    (mode === "file" && files.length > 0) ||
    (mode === "magnet" && isMagnetValid);

  return (
    <div>
      {/* Mode Switcher Tabs */}
      <div
        className="tab-nav"
        style={{
          display: "flex",
          gap: "0.5rem",
          marginBottom: "1.25rem",
          borderBottom: "1px solid var(--border-light)",
          paddingBottom: "0.5rem",
        }}
      >
        <button
          type="button"
          className={`tab-btn ${mode === "file" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("file")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          📁 Torrent File
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "magnet" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("magnet")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          🧲 Magnet Link
        </button>
        <button
          type="button"
          className={`tab-btn ${mode === "search" ? "tab-btn-active" : ""}`}
          onClick={() => setMode("search")}
          style={{
            fontSize: "0.9rem",
            padding: "0.45rem 1rem",
            borderRadius: "6px",
          }}
        >
          🔍 Indexer Search
        </button>
      </div>

      {/* Mode 1: File Upload */}
      {mode === "file" && (
        <div>
          <div
            className={`drop-zone ${isDragOver ? "drop-zone-active" : ""} ${files.length > 0 ? "drop-zone-has-file" : ""}`}
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            onClick={() => fileInputRef.current?.click()}
            style={{
              border: isDragOver
                ? "2px dashed var(--accent)"
                : "2px dashed rgba(255, 255, 255, 0.15)",
              borderRadius: "8px",
              padding: "2.5rem 1.5rem",
              textAlign: "center",
              cursor: "pointer",
              backgroundColor: isDragOver
                ? "rgba(200, 168, 78, 0.08)"
                : "var(--bg-primary)",
              transition: "all 0.2s ease",
            }}
          >
            <div style={{ fontSize: "2.2rem", marginBottom: "0.5rem" }}>📤</div>
            {files.length > 0 ? (
              <div>
                <span style={{ fontWeight: 600, color: "var(--accent)" }}>
                  {files.length === 1
                    ? `${files[0].name} selected`
                    : `${files.length} torrent files selected`}
                </span>
                <div
                  style={{
                    fontSize: "0.8rem",
                    color: "var(--text-muted)",
                    marginTop: "0.25rem",
                  }}
                >
                  Click or drag more files to add
                </div>
              </div>
            ) : (
              <div>
                <div style={{ fontWeight: 500, fontSize: "0.95rem" }}>
                  Drop .torrent files here or click to browse
                </div>
                <div
                  style={{
                    fontSize: "0.8rem",
                    color: "var(--text-muted)",
                    marginTop: "0.25rem",
                  }}
                >
                  Supports multiple .torrent files simultaneously
                </div>
              </div>
            )}
          </div>

          <input
            ref={fileInputRef}
            type="file"
            accept=".torrent"
            multiple
            style={{ display: "none" }}
            onChange={handleFileChange}
          />

          {files.length > 0 && (
            <div style={{ marginTop: "1rem" }}>
              <div
                style={{
                  fontSize: "0.8rem",
                  fontWeight: 600,
                  textTransform: "uppercase",
                  color: "var(--text-muted)",
                  marginBottom: "0.4rem",
                }}
              >
                Selected Files ({files.length})
              </div>
              <ul
                style={{
                  listStyle: "none",
                  padding: 0,
                  margin: 0,
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.4rem",
                  maxHeight: "180px",
                  overflowY: "auto",
                }}
              >
                {files.map((f) => (
                  <li
                    key={f.name}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      padding: "0.4rem 0.75rem",
                      backgroundColor: "var(--bg-primary)",
                      borderRadius: "6px",
                      border: "1px solid var(--border-light)",
                      fontSize: "0.85rem",
                    }}
                  >
                    <span
                      style={{
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                        marginRight: "0.5rem",
                      }}
                    >
                      📄 {f.name}
                    </span>
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        removeFile(f.name);
                      }}
                      style={{
                        background: "none",
                        border: "none",
                        color: "var(--danger)",
                        cursor: "pointer",
                        fontSize: "0.85rem",
                        padding: "0.1rem 0.3rem",
                      }}
                      title="Remove file"
                    >
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {/* Mode 2: Magnet Link */}
      {mode === "magnet" && (
        <div>
          <label
            style={{
              display: "block",
              fontSize: "0.85rem",
              fontWeight: 600,
              marginBottom: "0.5rem",
              color: "var(--text-primary)",
            }}
          >
            Magnet URI / Link
          </label>
          <textarea
            className="form-control"
            placeholder="magnet:?xt=urn:btih:..."
            value={magnetLink}
            onChange={(e) => setMagnetLink(e.target.value)}
            rows={4}
            style={{
              width: "100%",
              padding: "0.75rem",
              borderRadius: "6px",
              backgroundColor: "var(--bg-primary)",
              border: "1px solid var(--border-light)",
              color: "inherit",
              fontFamily: "monospace",
              fontSize: "0.85rem",
              resize: "vertical",
            }}
            autoFocus
          />
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginTop: "0.5rem",
              fontSize: "0.78rem",
            }}
          >
            <span style={{ color: "var(--text-muted)" }}>
              Paste any valid BitTorrent v1 or v2 magnet link.
            </span>
            {magnetLink.trim() && (
              <span
                style={{
                  color: isMagnetValid ? "var(--success)" : "var(--danger)",
                  fontWeight: 600,
                }}
              >
                {isMagnetValid
                  ? "✓ Valid Magnet Format"
                  : "✗ Must start with magnet:?"}
              </span>
            )}
          </div>
        </div>
      )}

      {/* Mode 3: Indexer Search */}
      {mode === "search" && (
        <div>
          {enabledIndexers.length === 0 ? (
            <div
              style={{
                padding: "2.5rem 1rem",
                textAlign: "center",
                backgroundColor: "var(--bg-primary)",
                borderRadius: "8px",
                border: "1px solid var(--border-light)",
              }}
            >
              <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>🔌</div>
              <div style={{ fontWeight: 600, marginBottom: "0.4rem" }}>
                No Enabled Indexers Configured
              </div>
              <p
                style={{
                  color: "var(--text-muted)",
                  fontSize: "0.85rem",
                  maxWidth: "420px",
                  margin: "0 auto 1.25rem",
                }}
              >
                Connect Jackett, Prowlarr, Torznab, or Newznab indexers in
                Settings to search releases directly.
              </p>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => {
                  if (onClose) onClose();
                  navigate("/settings/indexers");
                }}
              >
                ⚙️ Configure Indexers
              </button>
            </div>
          ) : (
            <div>
              <form
                onSubmit={handleSearchSubmit}
                style={{
                  display: "flex",
                  gap: "0.5rem",
                  flexWrap: "wrap",
                  marginBottom: "1rem",
                }}
              >
                <input
                  type="text"
                  className="form-control"
                  placeholder="Search releases (e.g. Ubuntu, Debian, release name)..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  style={{
                    flex: 1,
                    minWidth: "240px",
                    padding: "0.5rem 0.85rem",
                    borderRadius: "6px",
                    backgroundColor: "var(--bg-primary)",
                    border: "1px solid var(--border-light)",
                    color: "inherit",
                    fontSize: "0.9rem",
                  }}
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
                      backgroundColor: "var(--bg-primary)",
                      color: "inherit",
                      border: "1px solid var(--border-light)",
                      borderRadius: "6px",
                      padding: "0.5rem 0.85rem",
                      fontSize: "0.85rem",
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
                  style={{ borderRadius: "6px", padding: "0.5rem 1.25rem" }}
                >
                  {searchResults.isFetching ? "Searching..." : "Search"}
                </button>
              </form>

              {/* Results Container */}
              <div
                style={{
                  maxHeight: isModal ? "480px" : "620px",
                  overflowY: "auto",
                  border: "1px solid var(--border-light)",
                  borderRadius: "8px",
                  backgroundColor: "var(--bg-primary)",
                  boxShadow: "inset 0 2px 6px rgba(0, 0, 0, 0.2)",
                }}
              >
                {searchResults.isFetching && (
                  <div style={{ padding: "3rem", textAlign: "center" }}>
                    <div className="loading">
                      Searching configured indexers...
                    </div>
                  </div>
                )}

                {searchResults.isError && (
                  <div
                    style={{
                      padding: "2rem",
                      color: "var(--danger)",
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
                        padding: "3rem",
                        textAlign: "center",
                        color: "var(--text-muted)",
                      }}
                    >
                      No releases found for "{activeSearchTerm}". Try different
                      keywords or indexer.
                    </div>
                  )}

                {!searchResults.isFetching && !activeSearchTerm && (
                  <div
                    style={{
                      padding: "3rem",
                      textAlign: "center",
                      color: "var(--text-muted)",
                    }}
                  >
                    Type a keyword above to search across your configured
                    indexers ({enabledIndexers.map((i) => i.name).join(", ")}).
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
                            borderBottom: "1px solid var(--border-light)",
                            textAlign: "left",
                            fontSize: "0.8rem",
                            color: "var(--text-muted)",
                            position: "sticky",
                            top: 0,
                            backgroundColor: "var(--bg-primary)",
                            zIndex: 2,
                          }}
                        >
                          <th style={{ padding: "0.65rem 0.85rem" }}>Title</th>
                          <th
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "130px",
                            }}
                          >
                            Indexer
                          </th>
                          <th
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "100px",
                            }}
                          >
                            Size
                          </th>
                          <th
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "95px",
                            }}
                          >
                            Peers
                          </th>
                          <th
                            style={{
                              padding: "0.65rem 0.85rem",
                              width: "100px",
                            }}
                          >
                            Date
                          </th>
                          <th
                            style={{
                              padding: "0.65rem 0.85rem",
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
                          const itemKey = rel.guid || rel.infoHash || rel.title;
                          const isDownloading = downloadingGuid === itemKey;

                          return (
                            <tr
                              key={itemKey}
                              style={{
                                borderBottom:
                                  "1px solid rgba(255, 255, 255, 0.05)",
                                fontSize: "0.85rem",
                              }}
                            >
                              <td style={{ padding: "0.65rem 0.85rem" }}>
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
                                        marginTop: "0.25rem",
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
                                              padding: "0.1rem 0.35rem",
                                              borderRadius: "3px",
                                            }}
                                          >
                                            {c}
                                          </span>
                                        ))}
                                    </div>
                                  )}
                              </td>

                              <td style={{ padding: "0.65rem 0.85rem" }}>
                                <span
                                  className="badge badge-primary"
                                  style={{
                                    fontSize: "0.75rem",
                                    borderRadius: "4px",
                                  }}
                                >
                                  {rel.indexer || "Indexer"}
                                </span>
                              </td>

                              <td
                                style={{
                                  padding: "0.65rem 0.85rem",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                {formatBytes(rel.size)}
                              </td>

                              <td
                                style={{
                                  padding: "0.65rem 0.85rem",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                <span
                                  style={{
                                    color: "var(--success)",
                                    fontWeight: 600,
                                  }}
                                >
                                  ▲ {rel.seeders ?? 0}
                                </span>{" "}
                                <span
                                  style={{
                                    color: "var(--text-muted)",
                                    marginLeft: "0.2rem",
                                  }}
                                >
                                  ▼ {rel.leechers ?? 0}
                                </span>
                              </td>

                              <td
                                style={{
                                  padding: "0.65rem 0.85rem",
                                  fontSize: "0.8rem",
                                  color: "var(--text-muted)",
                                  whiteSpace: "nowrap",
                                }}
                              >
                                {rel.publishDate
                                  ? formatDate(rel.publishDate)
                                  : "-"}
                              </td>

                              <td
                                style={{
                                  padding: "0.65rem 0.85rem",
                                  textAlign: "right",
                                }}
                              >
                                <button
                                  type="button"
                                  className="btn btn-success"
                                  style={{
                                    fontSize: "0.78rem",
                                    padding: "0.3rem 0.65rem",
                                    borderRadius: "4px",
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
            </div>
          )}
        </div>
      )}

      {(addTorrent.isError || resultMessage) && (
        <div
          className="modal-error"
          style={{ marginTop: "1rem", borderRadius: "6px" }}
        >
          {addTorrent.isError
            ? addTorrent.error instanceof Error
              ? addTorrent.error.message
              : "Failed to add torrent"
            : resultMessage}
        </div>
      )}

      {mode !== "search" && (
        <div
          className="modal-actions"
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.5rem",
            marginTop: "1.25rem",
          }}
        >
          {isModal && onClose && (
            <button
              type="button"
              className="btn btn-outline"
              onClick={onClose}
              disabled={addTorrent.isPending}
              style={{ borderRadius: "6px" }}
            >
              Cancel
            </button>
          )}
          <button
            type="button"
            className="btn btn-success"
            onClick={handleSubmit}
            disabled={!canSubmit || addTorrent.isPending}
            style={{ borderRadius: "6px", padding: "0.45rem 1.25rem" }}
          >
            {addTorrent.isPending ? "Adding..." : "Add Torrent"}
          </button>
        </div>
      )}
    </div>
  );
}

export default AddTorrentForm;
