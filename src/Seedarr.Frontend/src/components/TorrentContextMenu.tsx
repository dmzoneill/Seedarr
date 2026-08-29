import { useState, useEffect } from "react";
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
}: TorrentContextMenuProps) {
  const [openSubmenu, setOpenSubmenu] = useState<string | null>(null);

  useEffect(() => {
    const handleClick = () => onClose();
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("click", handleClick);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("click", handleClick);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [onClose]);

  function handleCopy(text: string) {
    navigator.clipboard
      .writeText(text)
      .catch((err) => console.warn("Clipboard write failed:", err));
    onClose();
  }

  const ct = torrent;

  return (
    <div
      className="context-menu"
      style={{ left: x, top: y }}
      onClick={(e) => e.stopPropagation()}
    >
      {ct ? (
        <>
          {/* Pause / Resume */}
          {ct.active ? (
            <button
              className="context-menu-item"
              onClick={() => {
                onStop(ct.id);
                onClose();
              }}
            >
              Pause
            </button>
          ) : (
            <button
              className="context-menu-item"
              onClick={() => {
                onStart(ct.id);
                onClose();
              }}
            >
              Resume
            </button>
          )}
          <button
            className="context-menu-item"
            onClick={() => {
              onUpdate({ ...ct, forceStart: !ct.forceStart });
              onClose();
            }}
          >
            {ct.forceStart ? "✓ " : ""}Force Start
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              onAnnounce(ct.id);
              onClose();
            }}
          >
            Update Tracker
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              onRecheck(ct.id);
              onClose();
            }}
          >
            Force Recheck
          </button>
          {ct.progress < 1.0 && (
            <button
              className="context-menu-item"
              onClick={() => {
                onUpdate({ ...ct, progress: 1.0 });
                onClose();
              }}
            >
              Force Complete
            </button>
          )}

          <div className="context-menu-separator" />

          {/* Copy submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("copy")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            Copy ▶
            {openSubmenu === "copy" && (
              <div className="context-menu context-menu-submenu">
                <button
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.name)}
                >
                  Name
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.infoHash)}
                >
                  Info Hash
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => handleCopy(buildMagnetLink(ct))}
                >
                  Magnet Link
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => handleCopy(ct.trackerUrl ?? "")}
                >
                  Tracker URL
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
            Priority ▶
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

          {/* Speed Limits submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("speed")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            Speed Limits ▶
            {openSubmenu === "speed" && (
              <div className="context-menu context-menu-submenu">
                <button
                  className="context-menu-item"
                  onClick={() => {
                    const v = window.prompt(
                      "Upload limit (KB/s, 0=global):",
                      String(ct.uploadLimit),
                    );
                    if (v !== null)
                      onUpdate({ ...ct, uploadLimit: parseInt(v, 10) || 0 });
                    onClose();
                  }}
                >
                  Set Upload Limit...
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    const v = window.prompt(
                      "Download limit (KB/s, 0=global):",
                      String(ct.downloadLimit),
                    );
                    if (v !== null)
                      onUpdate({ ...ct, downloadLimit: parseInt(v, 10) || 0 });
                    onClose();
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
            Queue ▶
            {openSubmenu === "queue" && (
              <div className="context-menu context-menu-submenu">
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "top" });
                    onClose();
                  }}
                >
                  Top
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "up" });
                    onClose();
                  }}
                >
                  Up
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "down" });
                    onClose();
                  }}
                >
                  Down
                </button>
                <button
                  className="context-menu-item"
                  onClick={() => {
                    onMoveQueue({ id: ct.id, position: "bottom" });
                    onClose();
                  }}
                >
                  Bottom
                </button>
              </div>
            )}
          </div>

          <div className="context-menu-separator" />

          {/* Rename / Label / Toggles */}
          <button
            className="context-menu-item"
            onClick={() => {
              const n = window.prompt("Rename torrent:", ct.name);
              if (n !== null && n.trim()) onUpdate({ ...ct, name: n.trim() });
              onClose();
            }}
          >
            Rename...
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              const l = window.prompt("Set label:", ct.label ?? "");
              if (l !== null) onUpdate({ ...ct, label: l || null });
              onClose();
            }}
          >
            Set Label...{ct.label ? ` (${ct.label})` : ""}
          </button>

          <div className="context-menu-separator" />

          <button
            className="context-menu-item"
            onClick={() => {
              onUpdate({ ...ct, superSeeding: !ct.superSeeding });
              onClose();
            }}
          >
            {ct.superSeeding ? "Disable" : "Enable"} Super Seeding
          </button>
          <button
            className="context-menu-item"
            onClick={() => {
              onUpdate({ ...ct, sequentialDownload: !ct.sequentialDownload });
              onClose();
            }}
          >
            {ct.sequentialDownload ? "Disable" : "Enable"} Sequential Download
          </button>

          <div className="context-menu-separator" />

          {/* Remove submenu */}
          <div
            className="context-menu-item context-menu-submenu-trigger"
            onMouseEnter={() => setOpenSubmenu("remove")}
            onMouseLeave={() => setOpenSubmenu(null)}
          >
            Remove ▶
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
                  Remove Torrent
                </button>
                <button
                  className="context-menu-item context-menu-item-danger"
                  onClick={() => {
                    if (confirm(`Remove "${ct.name}" and all data?`))
                      onDelete({ id: ct.id, deleteFiles: true });
                    onClose();
                  }}
                >
                  Remove Torrent and Data
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
        Columns ▶
        {openSubmenu === "columns" && (
          <div className="context-menu context-menu-submenu context-menu-columns">
            {allColumns.map((col) => (
              <label key={col.key} className="column-menu-item">
                <input
                  type="checkbox"
                  checked={visibleColumns.has(col.key)}
                  onChange={() => onToggleColumn(col.key)}
                />
                {col.label}
              </label>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default TorrentContextMenu;
