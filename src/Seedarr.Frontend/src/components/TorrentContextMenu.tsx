import { useState, useEffect } from "react";
import { useNavigate } from "react-router";
import { useArrConnections, useDownloadHistory } from "../api/hooks";
import { getMediaDeepLink } from "../utils/arrLinks";
import { useTranslation } from "../i18n";
import { COLUMN_I18N_KEYS } from "./TorrentTable";
import PromptModal from "./PromptModal";
import type { Torrent } from "../api/types";

export interface TorrentContextMenuProps {
  x: number;
  y: number;
  torrent: Torrent | null;
  selectedTorrents?: Torrent[];
  visibleColumns: Set<string>;
  allColumns: ReadonlyArray<{ key: string; label: string }>;
  onClose: () => void;
  onToggleColumn: (key: string) => void;
  onResetSort?: () => void;
  onStart: (id: number) => void;
  onStop: (id: number) => void;
  onUpdate: (torrent: Torrent) => void;
  onAnnounce: (id: number) => void;
  onRecheck: (id: number) => void;
  onDelete: (payload: { id: number; deleteFiles?: boolean }) => void;
  onMoveQueue: (payload: {
    id: number;
    position: "top" | "up" | "down" | "bottom";
  }) => void;
  onBatchStart?: (ids: number[]) => void;
  onBatchStop?: (ids: number[]) => void;
  onBatchAnnounce?: (ids: number[]) => void;
  onBatchRecheck?: (ids: number[]) => void;
  onBatchDelete?: (payload: { ids: number[]; deleteFiles?: boolean }) => void;
  onBatchUpdate?: (torrents: Torrent[]) => void;
  onBatchMoveQueue?: (payload: {
    ids: number[];
    position: "top" | "up" | "down" | "bottom";
  }) => void;
  onSearchIndexers?: (query: string) => void;
}

function buildMagnetLink(t: Torrent): string {
  let magnet = `magnet:?xt=urn:btih:${t.infoHash}&dn=${encodeURIComponent(t.name)}`;
  if (t.trackerUrl) magnet += `&tr=${encodeURIComponent(t.trackerUrl)}`;
  return magnet;
}

function TorrentContextMenu({
  x,
  y,
  torrent,
  selectedTorrents,
  visibleColumns,
  allColumns,
  onClose,
  onToggleColumn,
  onResetSort,
  onStart,
  onStop,
  onUpdate,
  onAnnounce,
  onRecheck,
  onDelete,
  onMoveQueue,
  onBatchStart,
  onBatchStop,
  onBatchAnnounce,
  onBatchRecheck,
  onBatchDelete,
  onBatchUpdate,
  onBatchMoveQueue,
  onSearchIndexers,
}: TorrentContextMenuProps) {
  const { t } = useTranslation();
  const [openSubmenu, setOpenSubmenu] = useState<string | null>(null);
  const [promptConfig, setPromptConfig] = useState<{
    title: string;
    description?: string;
    initialValue: string | number;
    inputType: "text" | "number";
    min?: number;
    max?: number;
    step?: number;
    suffix?: string;
    validate?: (val: string) => string | null;
    onConfirm: (val: string) => void;
  } | null>(null);
  const navigate = useNavigate();

  const { data: history } = useDownloadHistory();
  const { data: arrConnections } = useArrConnections();

  const isMulti = Boolean(selectedTorrents && selectedTorrents.length > 1);
  const effectiveTorrents: Torrent[] = isMulti
    ? selectedTorrents!
    : torrent
      ? [torrent]
      : [];
  const countSuffix = isMulti ? ` (${effectiveTorrents.length})` : "";
  const ct =
    torrent || (effectiveTorrents.length > 0 ? effectiveTorrents[0] : null);

  useEffect(() => {
    if (promptConfig) return;
    const handleClick = () => onClose();
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        onClose();
      }
    };
    document.addEventListener("click", handleClick);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("click", handleClick);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [onClose, promptConfig]);

  function handleCopy(text: string) {
    navigator.clipboard
      .writeText(text)
      .catch((err) => console.warn("Clipboard write failed:", err));
    onClose();
  }

  const handleStartAll = () => {
    if (isMulti) {
      if (onBatchStart) {
        onBatchStart(effectiveTorrents.map((t) => t.id));
      } else {
        effectiveTorrents.forEach((t) => onStart(t.id));
      }
    } else if (ct) {
      onStart(ct.id);
    }
    onClose();
  };

  const handleStopAll = () => {
    if (isMulti) {
      if (onBatchStop) {
        onBatchStop(effectiveTorrents.map((t) => t.id));
      } else {
        effectiveTorrents.forEach((t) => onStop(t.id));
      }
    } else if (ct) {
      onStop(ct.id);
    }
    onClose();
  };

  const handleAnnounceAll = () => {
    if (isMulti) {
      if (onBatchAnnounce) {
        onBatchAnnounce(effectiveTorrents.map((t) => t.id));
      } else {
        effectiveTorrents.forEach((t) => onAnnounce(t.id));
      }
    } else if (ct) {
      onAnnounce(ct.id);
    }
    onClose();
  };

  const handleRecheckAll = () => {
    if (isMulti) {
      if (onBatchRecheck) {
        onBatchRecheck(effectiveTorrents.map((t) => t.id));
      } else {
        effectiveTorrents.forEach((t) => onRecheck(t.id));
      }
    } else if (ct) {
      onRecheck(ct.id);
    }
    onClose();
  };

  const handleDeleteAll = (deleteFiles: boolean) => {
    if (isMulti) {
      const ids = effectiveTorrents.map((t) => t.id);
      if (
        confirm(
          `Remove ${ids.length} selected torrents${deleteFiles ? " and all data" : ""}?`,
        )
      ) {
        if (onBatchDelete) {
          onBatchDelete({ ids, deleteFiles });
        } else {
          ids.forEach((id) => onDelete({ id, deleteFiles }));
        }
      }
    } else if (ct) {
      if (
        confirm(`Remove "${ct.name}"${deleteFiles ? " and all data" : ""}?`)
      ) {
        onDelete({ id: ct.id, deleteFiles });
      }
    }
    onClose();
  };

  const handleUpdateAll = (updater: (t: Torrent) => Torrent) => {
    if (isMulti) {
      const updated = effectiveTorrents.map(updater);
      if (onBatchUpdate) {
        onBatchUpdate(updated);
      } else {
        updated.forEach((t) => onUpdate(t));
      }
    } else if (ct) {
      onUpdate(updater(ct));
    }
    onClose();
  };

  const handleMoveQueueAll = (position: "top" | "up" | "down" | "bottom") => {
    if (isMulti) {
      const ids = effectiveTorrents.map((t) => t.id);
      if (position === "down" || position === "bottom") {
        ids.reverse();
      }
      if (onBatchMoveQueue) {
        onBatchMoveQueue({ ids, position });
      } else {
        ids.forEach((id) => onMoveQueue({ id, position }));
      }
    } else if (ct) {
      onMoveQueue({ id: ct.id, position });
    }
    onClose();
  };

  const historyMatch =
    !isMulti && ct
      ? history?.find(
          (h) =>
            (ct.infoHash &&
              h.infoHash?.toLowerCase() === ct.infoHash.toLowerCase()) ||
            h.title?.toLowerCase() === ct.name?.toLowerCase(),
        )
      : null;

  const arrLink = historyMatch
    ? getMediaDeepLink(historyMatch, arrConnections)
    : null;

  return (
    <>
      <div
        className="context-menu"
        style={{ left: x, top: y, display: promptConfig ? "none" : undefined }}
        onClick={(e) => e.stopPropagation()}
      >
        {effectiveTorrents.length > 0 ? (
          <>
            {/* Multi-selection header indicator */}
            {isMulti && (
              <div
                className="context-menu-header"
                style={{
                  padding: "0.4rem 0.75rem",
                  fontSize: "0.75rem",
                  fontWeight: 600,
                  color: "var(--accent, #38bdf8)",
                  borderBottom:
                    "1px solid var(--border, rgba(255, 255, 255, 0.1))",
                  marginBottom: "0.25rem",
                  letterSpacing: "0.02em",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                }}
              >
                ✓ {effectiveTorrents.length} items selected
              </div>
            )}

            {/* Quick Queue Reorder Action Bar */}
            <div
              className="context-menu-quick-queue"
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.25rem 0.5rem",
                gap: "4px",
                borderBottom:
                  "1px solid var(--border, rgba(255, 255, 255, 0.1))",
                marginBottom: "0.25rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                style={{
                  flex: 1,
                  padding: "0.2rem 0.35rem",
                  fontSize: "0.75rem",
                  textAlign: "center",
                }}
                onClick={() => handleMoveQueueAll("top")}
                title={t("torrents.contextMenu.top", undefined, "Move to Top")}
              >
                ⤒ {t("torrents.contextMenu.top", undefined, "Top")}
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                style={{
                  flex: 1,
                  padding: "0.2rem 0.35rem",
                  fontSize: "0.75rem",
                  textAlign: "center",
                }}
                onClick={() => handleMoveQueueAll("up")}
                title={t("torrents.contextMenu.up", undefined, "Move Up")}
              >
                ▲ {t("torrents.contextMenu.up", undefined, "Up")}
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                style={{
                  flex: 1,
                  padding: "0.2rem 0.35rem",
                  fontSize: "0.75rem",
                  textAlign: "center",
                }}
                onClick={() => handleMoveQueueAll("down")}
                title={t("torrents.contextMenu.down", undefined, "Move Down")}
              >
                ▼ {t("torrents.contextMenu.down", undefined, "Down")}
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                style={{
                  flex: 1,
                  padding: "0.2rem 0.35rem",
                  fontSize: "0.75rem",
                  textAlign: "center",
                }}
                onClick={() => handleMoveQueueAll("bottom")}
                title={t(
                  "torrents.contextMenu.bottom",
                  undefined,
                  "Move to Bottom",
                )}
              >
                ⤓ {t("torrents.contextMenu.bottom", undefined, "Bottom")}
              </button>
            </div>

            {/* Arr Direct Jump Link (single item only) */}
            {!isMulti && arrLink && (
              <button
                className="context-menu-item"
                style={{ fontWeight: 600, color: "var(--accent)" }}
                onClick={() => {
                  window.open(arrLink.url, "_blank", "noopener,noreferrer");
                  onClose();
                }}
              >
                🔗 {arrLink.label} ↗
              </button>
            )}

            {/* Pause / Resume */}
            {isMulti ? (
              <>
                <button className="context-menu-item" onClick={handleStartAll}>
                  {t("torrents.resume", undefined, "Resume")}
                  {countSuffix}
                </button>
                <button className="context-menu-item" onClick={handleStopAll}>
                  {t("torrents.pause", undefined, "Pause")}
                  {countSuffix}
                </button>
              </>
            ) : ct?.active ? (
              <button className="context-menu-item" onClick={handleStopAll}>
                {t("torrents.pause", undefined, "Pause")}
              </button>
            ) : (
              <button className="context-menu-item" onClick={handleStartAll}>
                {t("torrents.resume", undefined, "Resume")}
              </button>
            )}

            <button
              className="context-menu-item"
              onClick={() =>
                handleUpdateAll((t) => ({ ...t, forceStart: !t.forceStart }))
              }
            >
              {!isMulti && ct?.forceStart ? "✓ " : ""}
              {t("torrents.forceStart", undefined, "Force Start")}
              {countSuffix}
            </button>
            <button className="context-menu-item" onClick={handleAnnounceAll}>
              {t("torrents.updateTracker", undefined, "Update Tracker")}
              {countSuffix}
            </button>
            <button className="context-menu-item" onClick={handleRecheckAll}>
              {t("torrents.forceRecheck", undefined, "Force Recheck")}
              {countSuffix}
            </button>
            {(!isMulti && ct && ct.progress < 1.0) ||
            (isMulti && effectiveTorrents.some((t) => t.progress < 1.0)) ? (
              <button
                className="context-menu-item"
                onClick={() =>
                  handleUpdateAll((t) => ({ ...t, progress: 1.0 }))
                }
              >
                {t("torrents.forceComplete", undefined, "Force Complete")}
                {countSuffix}
              </button>
            ) : null}

            <div className="context-menu-separator" />

            {/* Usability & Navigation Actions (single selection only) */}
            {!isMulti && ct && (
              <>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    if (onSearchIndexers) {
                      onSearchIndexers(ct.name);
                    }
                    onClose();
                  }}
                >
                  🔍{" "}
                  {t(
                    "torrents.searchIndexers",
                    undefined,
                    "Search on Indexers",
                  )}
                </button>

                <button
                  className="context-menu-item"
                  onClick={() => {
                    navigate("/peermap");
                    onClose();
                  }}
                >
                  🗺️{" "}
                  {t("torrents.trackInPeerMap", undefined, "Track in Peer Map")}
                </button>

                <div className="context-menu-separator" />
              </>
            )}

            {/* Copy submenu */}
            <div
              className="context-menu-item context-menu-submenu-trigger"
              onMouseEnter={() => setOpenSubmenu("copy")}
              onMouseLeave={() => setOpenSubmenu(null)}
            >
              {t("common.copy", undefined, "Copy")}
              {countSuffix} ▶
              {openSubmenu === "copy" && (
                <div className="context-menu context-menu-submenu">
                  <button
                    className="context-menu-item"
                    onClick={() =>
                      handleCopy(
                        effectiveTorrents.map((t) => t.name).join("\n"),
                      )
                    }
                  >
                    {t("common.name", undefined, "Name")}
                    {countSuffix}
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() =>
                      handleCopy(
                        effectiveTorrents.map((t) => t.infoHash).join("\n"),
                      )
                    }
                  >
                    {t("torrents.table.infoHash", undefined, "Info Hash")}
                    {countSuffix}
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() =>
                      handleCopy(
                        effectiveTorrents
                          .map((t) => buildMagnetLink(t))
                          .join("\n"),
                      )
                    }
                  >
                    {t("modals.addTorrent.byMagnet", undefined, "Magnet Link")}
                    {countSuffix}
                  </button>
                  {!isMulti && ct && (
                    <button
                      className="context-menu-item"
                      onClick={() => handleCopy(ct.trackerUrl ?? "")}
                    >
                      {t("torrents.table.tracker", undefined, "Tracker URL")}
                    </button>
                  )}
                </div>
              )}
            </div>

            {/* Priority submenu */}
            <div
              className="context-menu-item context-menu-submenu-trigger"
              onMouseEnter={() => setOpenSubmenu("priority")}
              onMouseLeave={() => setOpenSubmenu(null)}
            >
              {t("torrents.priority", undefined, "Priority")}
              {countSuffix} ▶
              {openSubmenu === "priority" && (
                <div className="context-menu context-menu-submenu">
                  <button
                    className="context-menu-item"
                    onClick={() =>
                      handleUpdateAll((t) => ({ ...t, priority: 2 }))
                    }
                  >
                    {!isMulti && ct?.priority === 2 ? "✓ " : ""}High
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() =>
                      handleUpdateAll((t) => ({ ...t, priority: 1 }))
                    }
                  >
                    {!isMulti && ct?.priority === 1 ? "✓ " : ""}Normal
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() =>
                      handleUpdateAll((t) => ({ ...t, priority: 0 }))
                    }
                  >
                    {!isMulti && ct?.priority === 0 ? "✓ " : ""}Low
                  </button>
                </div>
              )}
            </div>

            {/* Speed Limit submenu */}
            <div
              className="context-menu-item context-menu-submenu-trigger"
              onMouseEnter={() => setOpenSubmenu("speed")}
              onMouseLeave={() => setOpenSubmenu(null)}
            >
              Speed Limit{countSuffix} ▶
              {openSubmenu === "speed" && (
                <div className="context-menu context-menu-submenu">
                  <button
                    className="context-menu-item"
                    onClick={() => {
                      setPromptConfig({
                        title: `${t("torrents.setUploadLimit", undefined, "Set Upload Speed Limit")}${countSuffix}`,
                        description:
                          "Enter upload limit in KB/s (0 for unlimited):",
                        initialValue: !isMulti ? ct?.uploadLimit || 0 : 0,
                        inputType: "number",
                        min: 0,
                        suffix: "KB/s",
                        onConfirm: (limit) => {
                          const val = parseInt(limit, 10);
                          if (!isNaN(val) && val >= 0) {
                            handleUpdateAll((t) => ({
                              ...t,
                              uploadLimit: val,
                            }));
                          }
                        },
                      });
                    }}
                  >
                    Set Upload Limit...{countSuffix}
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() => {
                      setPromptConfig({
                        title: `${t("torrents.setDownloadLimit", undefined, "Set Download Speed Limit")}${countSuffix}`,
                        description:
                          "Enter download limit in KB/s (0 for unlimited):",
                        initialValue: !isMulti ? ct?.downloadLimit || 0 : 0,
                        inputType: "number",
                        min: 0,
                        suffix: "KB/s",
                        onConfirm: (limit) => {
                          const val = parseInt(limit, 10);
                          if (!isNaN(val) && val >= 0) {
                            handleUpdateAll((t) => ({
                              ...t,
                              downloadLimit: val,
                            }));
                          }
                        },
                      });
                    }}
                  >
                    Set Download Limit...{countSuffix}
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() =>
                      handleUpdateAll((t) => ({
                        ...t,
                        uploadLimit: 0,
                        downloadLimit: 0,
                      }))
                    }
                  >
                    Reset to Global Limits{countSuffix}
                  </button>
                </div>
              )}
            </div>

            {/* Queue submenu */}
            <div
              className="context-menu-item context-menu-submenu-trigger"
              onMouseEnter={() => setOpenSubmenu("queue")}
              onMouseLeave={() => setOpenSubmenu(null)}
            >
              {t("torrents.queue", undefined, "Queue")}
              {countSuffix} ▶
              {openSubmenu === "queue" && (
                <div className="context-menu context-menu-submenu">
                  <button
                    className="context-menu-item"
                    onClick={() => handleMoveQueueAll("top")}
                  >
                    {t("common.top", undefined, "Top")}
                    {countSuffix}
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() => handleMoveQueueAll("up")}
                  >
                    {t("common.up", undefined, "Up")}
                    {countSuffix}
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() => handleMoveQueueAll("down")}
                  >
                    {t("common.down", undefined, "Down")}
                    {countSuffix}
                  </button>
                  <button
                    className="context-menu-item"
                    onClick={() => handleMoveQueueAll("bottom")}
                  >
                    {t("common.bottom", undefined, "Bottom")}
                    {countSuffix}
                  </button>
                </div>
              )}
            </div>

            <div className="context-menu-separator" />

            {/* Rename (single selection only) */}
            {!isMulti && ct && (
              <button
                className="context-menu-item"
                onClick={() => {
                  setPromptConfig({
                    title: t("torrents.rename", undefined, "Rename Torrent"),
                    description: "Enter a new name for this torrent:",
                    initialValue: ct.name,
                    inputType: "text",
                    validate: (val) =>
                      !val.trim() ? "Name cannot be empty" : null,
                    onConfirm: (n) => {
                      if (n.trim()) onUpdate({ ...ct, name: n.trim() });
                      onClose();
                    },
                  });
                }}
              >
                {t("torrents.rename", undefined, "Rename...")}
              </button>
            )}

            {/* Set Location */}
            <button
              className="context-menu-item"
              onClick={() => {
                setPromptConfig({
                  title: `${t("torrents.setLocation", undefined, "Set Location")}${countSuffix}`,
                  description: "Enter new save path directory:",
                  initialValue: !isMulti ? (ct?.savePath || ct?.sourcePath || "") : "",
                  inputType: "text",
                  validate: (val) =>
                    !val.trim() ? "Location cannot be empty" : null,
                  onConfirm: (loc) => {
                    const trimmed = loc.trim();
                    if (trimmed) {
                      handleUpdateAll((t) => ({
                        ...t,
                        savePath: trimmed,
                        sourcePath: trimmed,
                      }));
                    }
                  },
                });
              }}
            >
              {t("torrents.setLocation", undefined, "Set Location...")}
              {countSuffix}
            </button>

            {/* Label / Tag */}
            <button
              className="context-menu-item"
              onClick={() => {
                setPromptConfig({
                  title: `${t("torrents.setLabel", undefined, "Set Torrent Label / Tag")}${countSuffix}`,
                  description:
                    "Enter label for organization (leave empty to remove):",
                  initialValue: !isMulti ? (ct?.label ?? "") : "",
                  inputType: "text",
                  onConfirm: (l) => {
                    const trimmed = l.trim() || null;
                    handleUpdateAll((t) => ({ ...t, label: trimmed }));
                  },
                });
              }}
            >
              {t("torrents.setLabel", undefined, "Set Label...")}
              {!isMulti && ct?.label ? ` (${ct.label})` : countSuffix}
            </button>

            {/* Export .torrent */}
            {ct && (
              <button
                className="context-menu-item"
                onClick={() => {
                  effectiveTorrents.forEach((t) => {
                    const link = document.createElement("a");
                    link.href = `/api/v1/torrent/${t.id}/torrent`;
                    link.download = `${t.name}.torrent`;
                    document.body.appendChild(link);
                    link.click();
                    document.body.removeChild(link);
                  });
                  onClose();
                }}
              >
                {t("torrents.exportTorrent", undefined, "Export .torrent")}
                {countSuffix}
              </button>
            )}

            <div className="context-menu-separator" />

            {/* Toggles */}
            <button
              className="context-menu-item"
              onClick={() => {
                if (isMulti) {
                  const anyDisabled = effectiveTorrents.some(
                    (t) => !t.superSeeding,
                  );
                  handleUpdateAll((t) => ({ ...t, superSeeding: anyDisabled }));
                } else if (ct) {
                  handleUpdateAll((t) => ({
                    ...t,
                    superSeeding: !t.superSeeding,
                  }));
                }
              }}
            >
              {isMulti
                ? `Super Seeding${countSuffix}`
                : `${ct?.superSeeding ? "Disable" : "Enable"} ${t("torrents.superSeeding", undefined, "Super Seeding")}`}
            </button>
            <button
              className="context-menu-item"
              onClick={() => {
                if (isMulti) {
                  const anyDisabled = effectiveTorrents.some(
                    (t) => !t.sequentialDownload,
                  );
                  handleUpdateAll((t) => ({
                    ...t,
                    sequentialDownload: anyDisabled,
                  }));
                } else if (ct) {
                  handleUpdateAll((t) => ({
                    ...t,
                    sequentialDownload: !t.sequentialDownload,
                  }));
                }
              }}
            >
              {isMulti
                ? `Sequential Download${countSuffix}`
                : `${ct?.sequentialDownload ? "Disable" : "Enable"} ${t("torrents.sequentialDownload", undefined, "Sequential Download")}`}
            </button>
            <button
              className="context-menu-item"
              onClick={() => {
                if (isMulti) {
                  const anyDisabled = effectiveTorrents.some(
                    (t) => !t.firstLastPiecePrio,
                  );
                  handleUpdateAll((t) => ({
                    ...t,
                    firstLastPiecePrio: anyDisabled,
                  }));
                } else if (ct) {
                  handleUpdateAll((t) => ({
                    ...t,
                    firstLastPiecePrio: !t.firstLastPiecePrio,
                  }));
                }
              }}
            >
              {isMulti
                ? `Prioritize First & Last Pieces${countSuffix}`
                : `${ct?.firstLastPiecePrio ? "Disable" : "Enable"} ${t("torrents.firstLastPiecePrio", undefined, "Prioritize First & Last Pieces")}`}
            </button>

            <div className="context-menu-separator" />

            {/* Remove submenu */}
            <div
              className="context-menu-item context-menu-submenu-trigger"
              onMouseEnter={() => setOpenSubmenu("remove")}
              onMouseLeave={() => setOpenSubmenu(null)}
            >
              {t("common.remove", undefined, "Remove")}
              {countSuffix} ▶
              {openSubmenu === "remove" && (
                <div className="context-menu context-menu-submenu">
                  <button
                    className="context-menu-item context-menu-item-danger"
                    onClick={() => handleDeleteAll(false)}
                  >
                    {t("torrents.removeTorrent", undefined, "Remove Torrent")}
                    {countSuffix}
                  </button>
                  <button
                    className="context-menu-item context-menu-item-danger"
                    onClick={() => handleDeleteAll(true)}
                  >
                    {t(
                      "torrents.removeTorrentAndData",
                      undefined,
                      "Remove Torrent and Data",
                    )}
                    {countSuffix}
                  </button>
                </div>
              )}
            </div>

            <div className="context-menu-separator" />
          </>
        ) : null}

        {/* Columns section */}
        {allColumns.length > 0 && (
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("columns")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            {t("torrents.columns", undefined, "Columns")} ▶
            {openSubmenu === "columns" && (
              <div className="context-menu context-menu-submenu context-menu-columns">
                {allColumns.map((col) => (
                  <label key={col.key} className="column-menu-item">
                    <input
                      type="checkbox"
                      checked={visibleColumns.has(col.key)}
                      onChange={() => onToggleColumn(col.key)}
                    />
                    {t(
                      COLUMN_I18N_KEYS[
                        col.key as keyof typeof COLUMN_I18N_KEYS
                      ] || `torrents.table.${col.key}`,
                      undefined,
                      col.label,
                    )}
                  </label>
                ))}
              </div>
            )}
          </div>
        )}

        {onResetSort && (
          <div
            className="context-menu-item"
            onClick={() => {
              onResetSort();
              onClose();
            }}
          >
            {t("torrents.resetSort", undefined, "Reset Sort Order")}
          </div>
        )}
      </div>
      {promptConfig && (
        <PromptModal
          isOpen={true}
          title={promptConfig.title}
          description={promptConfig.description}
          initialValue={promptConfig.initialValue}
          inputType={promptConfig.inputType}
          min={promptConfig.min}
          max={promptConfig.max}
          step={promptConfig.step}
          suffix={promptConfig.suffix}
          validate={promptConfig.validate}
          onConfirm={promptConfig.onConfirm}
          onClose={() => {
            setPromptConfig(null);
            onClose();
          }}
        />
      )}
    </>
  );
}

export default TorrentContextMenu;
