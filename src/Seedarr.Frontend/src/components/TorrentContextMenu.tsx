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
  visibleColumns: Set<string>;
  allColumns: ReadonlyArray<{ key: string; label: string }>;
  onClose: () => void;
  onToggleColumn: (key: string) => void;
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
  visibleColumns,
  allColumns,
  onClose,
  onToggleColumn,
  onStart,
  onStop,
  onUpdate,
  onAnnounce,
  onRecheck,
  onDelete,
  onMoveQueue,
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

  const ct = torrent;

  const historyMatch = ct
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
      {ct ? (
        <>
          {/* Arr Direct Jump Link */}
          {arrLink && (
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
          {ct.active ? (
            <button
              className="context-menu-item"
              onClick={() => {
                onStop(ct.id);
                onClose();
              }}
            >
              {t("torrents.pause", undefined, "Pause")}
            </button>
          ) : (
            <button
              className="context-menu-item"
              onClick={() => {
                onStart(ct.id);
                onClose();
              }}
            >
              {t("torrents.resume", undefined, "Resume")}
            </button>
          )}
          <button
            className="context-menu-item"
            onClick={() => {
              onUpdate({ ...ct, forceStart: !ct.forceStart });
              onClose();
            }}
          >
            {ct.forceStart ? "✓ " : ""}{t("torrents.forceStart", undefined, "Force Start")}
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              onAnnounce(ct.id);
              onClose();
            }}
          >
            {t("torrents.updateTracker", undefined, "Update Tracker")}
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              onRecheck(ct.id);
              onClose();
            }}
          >
            {t("torrents.forceRecheck", undefined, "Force Recheck")}
          </button>
          {ct.progress < 1.0 && (
            <button
              className="context-menu-item"
              onClick={() => {
                onUpdate({ ...ct, progress: 1.0 });
                onClose();
              }}
            >
              {t("torrents.forceComplete", undefined, "Force Complete")}
            </button>
          )}

          <div className="context-menu-separator" />

          {/* Usability & Navigation Actions */}
          <button
            className="context-menu-item"
            onClick={() => {
              if (onSearchIndexers) {
                onSearchIndexers(ct.name);
              }
              onClose();
            }}
          >
            🔍 {t("torrents.searchIndexers", undefined, "Search on Indexers")}
          </button>

          <button
            className="context-menu-item"
            onClick={() => {
              navigate("/peermap");
              onClose();
            }}
          >
            🗺️ {t("torrents.trackInPeerMap", undefined, "Track in Peer Map")}
          </button>

          <div className="context-menu-separator" />

          {/* Copy submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("copy")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            {t("common.copy", undefined, "Copy")} ▶
            {openSubmenu === "copy" && (
              <div className="context-menu context-menu-submenu">
                <button
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.name)}
                >
                  {t("common.name", undefined, "Name")}
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.infoHash)}
                >
                  {t("torrents.table.infoHash", undefined, "Info Hash")}
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => handleCopy(buildMagnetLink(ct))}
                >
                  {t("modals.addTorrent.byMagnet", undefined, "Magnet Link")}
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.trackerUrl ?? "")}
                >
                  {t("torrents.table.tracker", undefined, "Tracker URL")}
                </button>
              </div>
            )}
          </div>

          {/* Priority submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("priority")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            {t("torrents.priority", undefined, "Priority")} ▶
            {openSubmenu === "priority" && (
              <div className="context-menu context-menu-submenu">
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onUpdate({ ...ct, priority: 2 });
                    onClose();
                  }}
                >
                  {ct.priority === 2 ? "✓ " : ""}High
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onUpdate({ ...ct, priority: 1 });
                    onClose();
                  }}
                >
                  {ct.priority === 1 ? "✓ " : ""}Normal
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onUpdate({ ...ct, priority: 0 });
                    onClose();
                  }}
                >
                  {ct.priority === 0 ? "✓ " : ""}Low
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
            Speed Limit ▶
            {openSubmenu === "speed" && (
              <div className="context-menu context-menu-submenu">
                <button
                  className="context-menu-item"
                  onClick={() => {
                    setPromptConfig({
                      title: t("torrents.setUploadLimit", undefined, "Set Upload Speed Limit"),
                      description: "Enter upload limit in KB/s (0 for unlimited):",
                      initialValue: ct.uploadLimit || 0,
                      inputType: "number",
                      min: 0,
                      suffix: "KB/s",
                      onConfirm: (limit) => {
                        const val = parseInt(limit, 10);
                        if (!isNaN(val) && val >= 0)
                          onUpdate({ ...ct, uploadLimit: val });
                        onClose();
                      },
                    });
                  }}
                >
                  Set Upload Limit...
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    setPromptConfig({
                      title: t("torrents.setDownloadLimit", undefined, "Set Download Speed Limit"),
                      description: "Enter download limit in KB/s (0 for unlimited):",
                      initialValue: ct.downloadLimit || 0,
                      inputType: "number",
                      min: 0,
                      suffix: "KB/s",
                      onConfirm: (limit) => {
                        const val = parseInt(limit, 10);
                        if (!isNaN(val) && val >= 0)
                          onUpdate({ ...ct, downloadLimit: val });
                        onClose();
                      },
                    });
                  }}
                >
                  Set Download Limit...
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onUpdate({ ...ct, uploadLimit: 0, downloadLimit: 0 });
                    onClose();
                  }}
                >
                  Reset to Global Limits
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
            {t("torrents.queue", undefined, "Queue")} ▶
            {openSubmenu === "queue" && (
              <div className="context-menu context-menu-submenu">
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "top" });
                    onClose();
                  }}
                >
                  {t("common.top", undefined, "Top")}
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "up" });
                    onClose();
                  }}
                >
                  {t("common.up", undefined, "Up")}
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "down" });
                    onClose();
                  }}
                >
                  {t("common.down", undefined, "Down")}
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "bottom" });
                    onClose();
                  }}
                >
                  {t("common.bottom", undefined, "Bottom")}
                </button>
              </div>
            )}
          </div>

          <div className="context-menu-separator" />

          {/* Rename / Label / Toggles */}
          <button
            className="context-menu-item"
            onClick={() => {
              setPromptConfig({
                title: t("torrents.rename", undefined, "Rename Torrent"),
                description: "Enter a new name for this torrent:",
                initialValue: ct.name,
                inputType: "text",
                validate: (val) => (!val.trim() ? "Name cannot be empty" : null),
                onConfirm: (n) => {
                  if (n.trim()) onUpdate({ ...ct, name: n.trim() });
                  onClose();
                },
              });
            }}
          >
            {t("torrents.rename", undefined, "Rename...")}
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              setPromptConfig({
                title: t("torrents.setLabel", undefined, "Set Torrent Label / Tag"),
                description: "Enter label for organization (leave empty to remove):",
                initialValue: ct.label ?? "",
                inputType: "text",
                onConfirm: (l) => {
                  onUpdate({ ...ct, label: l.trim() || null });
                  onClose();
                },
              });
            }}
          >
            {t("torrents.setLabel", undefined, "Set Label...")}{ct.label ? ` (${ct.label})` : ""}
          </button>

          <div className="context-menu-separator" />

          <button
            className="context-menu-item"
            onClick={() => {
              onUpdate({ ...ct, superSeeding: !ct.superSeeding });
              onClose();
            }}
          >
            {ct.superSeeding ? "Disable" : "Enable"} {t("torrents.superSeeding", undefined, "Super Seeding")}
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              onUpdate({ ...ct, sequentialDownload: !ct.sequentialDownload });
              onClose();
            }}
          >
            {ct.sequentialDownload ? "Disable" : "Enable"} {t("torrents.sequentialDownload", undefined, "Sequential Download")}
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              onUpdate({ ...ct, firstLastPiecePrio: !ct.firstLastPiecePrio });
              onClose();
            }}
          >
            {ct.firstLastPiecePrio ? "Disable" : "Enable"} {t("torrents.firstLastPiecePrio", undefined, "Prioritize First & Last Pieces")}
          </button>

          <div className="context-menu-separator" />

          {/* Remove submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("remove")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            {t("common.remove", undefined, "Remove")} ▶
            {openSubmenu === "remove" && (
              <div className="context-menu context-menu-submenu">
                <button
                  className="context-menu-item context-menu-item-danger"
                  onClick={() => {
                    if (confirm(`Remove "${ct.name}"?`))
                      onDelete({ id: ct.id });
                    onClose();
                  }}
                >
                  {t("torrents.removeTorrent", undefined, "Remove Torrent")}
                </button>
                <button
                  className="context-menu-item context-menu-item-danger"
                  onClick={() => {
                    if (confirm(`Remove "${ct.name}" and all data?`))
                      onDelete({ id: ct.id, deleteFiles: true });
                    onClose();
                  }}
                >
                  {t("torrents.removeTorrentAndData", undefined, "Remove Torrent and Data")}
                </button>
              </div>
            )}
          </div>

          <div className="context-menu-separator" />
        </>
      ) : null}

      {/* Columns section - always shown */}
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
                {t(COLUMN_I18N_KEYS[col.key as keyof typeof COLUMN_I18N_KEYS] || `torrents.table.${col.key}`, undefined, col.label)}
              </label>
            ))}
          </div>
        )}
      </div>
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
