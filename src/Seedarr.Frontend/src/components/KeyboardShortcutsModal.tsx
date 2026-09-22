import { useEffect } from "react";
import { useTranslation } from "../i18n";
import { useModalRegistration } from "./ModalProvider";
import { trackModalOpen } from "../utils/analytics";
import { useFocusTrap } from "../hooks/useFocusTrap";

interface KeyboardShortcutsModalProps {
  isOpen: boolean;
  onClose: () => void;
}

interface ShortcutGroup {
  name: string;
  shortcuts: { keys: string[]; description: string }[];
}

export function KeyboardShortcutsModal({
  isOpen,
  onClose,
}: KeyboardShortcutsModalProps) {
  const { t } = useTranslation();
  const modalRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onEscape: onClose,
  });

  useEffect(() => {
    if (isOpen) {
      trackModalOpen("keyboard_shortcuts");
    }
  }, [isOpen]);

  useModalRegistration({
    id: "keyboard-shortcuts-modal",
    isOpen,
    onClose,
    modalRef,
  });

  if (!isOpen) return null;

  const groups: ShortcutGroup[] = [
    {
      name: t("keyboardShortcuts.categories.globalNavigation", "Global Navigation"),
      shortcuts: [
        {
          keys: ["Ctrl / ⌘", "K"],
          description: t("keyboardShortcuts.shortcuts.openCommandPalette", "Open Command Palette / Quick Jump"),
        },
        { keys: ["/"], description: t("keyboardShortcuts.shortcuts.focusSearch", "Focus Search / Quick Jump") },
        {
          keys: ["Alt / ⌥", "M"],
          description: t("keyboardShortcuts.shortcuts.toggleSidebar", "Toggle Main Navigation Sidebar"),
        },
        { keys: ["g", "d"], description: t("keyboardShortcuts.shortcuts.goDashboard", "Go to Dashboard") },
        { keys: ["g", "t"], description: t("keyboardShortcuts.shortcuts.goTorrents", "Go to Torrents Index") },
        { keys: ["g", "h"], description: t("keyboardShortcuts.shortcuts.goHistory", "Go to Download History") },
        { keys: ["g", "b"], description: t("keyboardShortcuts.shortcuts.goTrackerBoost", "Go to Tracker Boost") },
        { keys: ["g", "m"], description: t("keyboardShortcuts.shortcuts.goMetrics", "Go to Activity Metrics") },
        { keys: ["g", "p"], description: t("keyboardShortcuts.shortcuts.goPeerMap", "Go to Peer Map") },
        { keys: ["g", "s"], description: t("keyboardShortcuts.shortcuts.goSettings", "Go to Settings") },
        { keys: ["g", "c"], description: t("keyboardShortcuts.shortcuts.goTerminal", "Go to System Terminal") },
      ],
    },
    {
      name: t("keyboardShortcuts.categories.torrentTable", "Torrent Table Navigation & Selection"),
      shortcuts: [
        { keys: ["↑", "↓"], description: t("keyboardShortcuts.shortcuts.navTableRows", "Navigate up / down table rows") },
        {
          keys: ["Shift", "↑ / ↓"],
          description: t("keyboardShortcuts.shortcuts.rangeSelection", "Contiguous range selection expansion & contraction"),
        },
        { keys: ["Home", "End"], description: t("keyboardShortcuts.shortcuts.jumpFirstLast", "Jump to first / last row") },
        {
          keys: ["Enter"],
          description: t("keyboardShortcuts.shortcuts.openDetailsPanel", "Open details panel for focused torrent"),
        },
        {
          keys: ["Ctrl / ⌘", "A"],
          description: t("keyboardShortcuts.shortcuts.selectAllFiltered", "Select all filtered torrents"),
        },
        {
          keys: ["Ctrl / ⌘", "Shift", "I"],
          description: t("keyboardShortcuts.shortcuts.invertSelection", "Invert selection across torrents"),
        },
        {
          keys: ["Space"],
          description: t("keyboardShortcuts.shortcuts.togglePauseResume", "Toggle Pause / Resume on selected torrent(s)"),
        },
        {
          keys: ["Shift / Ctrl", "Space"],
          description: t("keyboardShortcuts.shortcuts.toggleRowCheckbox", "Toggle selection checkbox for focused row"),
        },
      ],
    },
    {
      name: t("keyboardShortcuts.categories.queuePriority", "Queue Priority & Operations"),
      shortcuts: [
        {
          keys: ["Ctrl / ⌘", "↑ / ↓"],
          description: t("keyboardShortcuts.shortcuts.queuePriorityUpDown", "Move selected torrent up / down in queue priority"),
        },
        {
          keys: ["Ctrl / ⌘", "Shift", "↑ / ↓"],
          description: t("keyboardShortcuts.shortcuts.queuePriorityTopBottom", "Move selected torrent to top / bottom of queue"),
        },
        {
          keys: ["Ctrl / ⌘", "R / F5"],
          description: t("keyboardShortcuts.shortcuts.forceRecheck", "Force recheck hash on selected torrent(s)"),
        },
        {
          keys: ["F6 / a"],
          description: t("keyboardShortcuts.shortcuts.forceAnnounce", "Force announce to all trackers"),
        },
        {
          keys: ["Ctrl / ⌘", "T"],
          description: t("keyboardShortcuts.shortcuts.openBulkTag", "Open Bulk Tag modal for selected torrent(s)"),
        },
        {
          keys: ["Delete / Backspace"],
          description: t("keyboardShortcuts.shortcuts.deleteTorrent", "Delete selected torrent(s)"),
        },
      ],
    },
    {
      name: t("keyboardShortcuts.categories.detailPanel", "Detail Panel & Files Navigation"),
      shortcuts: [
        {
          keys: ["←", "→"],
          description: t("keyboardShortcuts.shortcuts.cycleTabs", "Cycle tabs when detail panel tab bar is focused"),
        },
        {
          keys: ["↑", "↓"],
          description: t("keyboardShortcuts.shortcuts.navFiles", "Navigate files in Files tab"),
        },
        {
          keys: ["Space"],
          description: t("keyboardShortcuts.shortcuts.toggleFileWanted", "Toggle file download wanted in Files tab"),
        },
        {
          keys: ["1", "2", "0"],
          description: t("keyboardShortcuts.shortcuts.setFilePriority", "Set file priority: 1=Normal, 2=High, 0=Do Not Download"),
        },
      ],
    },
    {
      name: t("keyboardShortcuts.categories.terminalModals", "Terminal & Modals"),
      shortcuts: [
        {
          keys: ["Ctrl / ⌘", "L"],
          description: t("keyboardShortcuts.shortcuts.clearTerminal", "Clear terminal buffer"),
        },
        {
          keys: ["Ctrl / ⌘", "C"],
          description: t("keyboardShortcuts.shortcuts.interruptTerminal", "Interrupt command in terminal"),
        },
        {
          keys: ["q"],
          description: t("keyboardShortcuts.shortcuts.toggleQuickControls", "Toggle Quick Controls drawer"),
        },
        {
          keys: ["?"],
          description: t("keyboardShortcuts.shortcuts.showShortcuts", "Show this Keyboard Shortcuts cheat sheet"),
        },
        {
          keys: ["Esc"],
          description: t("keyboardShortcuts.shortcuts.closeActiveModal", "Close active modal, detail panel, or exit code editor"),
        },
      ],
    },
  ];

  return (
    <div
      ref={modalRef}
      role="dialog"
      aria-modal="true"
      aria-labelledby="keyboard-shortcuts-title"
      tabIndex={-1}
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.75)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 9999,
        padding: "1rem",
      }}
      onClick={onClose}
    >
      <div
        className="card"
        style={{
          width: "580px",
          maxWidth: "92vw",
          maxHeight: "85vh",
          display: "flex",
          flexDirection: "column",
          borderRadius: "12px",
          overflow: "hidden",
          border: "1px solid var(--border-light)",
          boxShadow: "0 16px 48px rgba(0, 0, 0, 0.6)",
          padding: 0,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Modal Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            padding: "1rem 1.25rem",
            borderBottom: "1px solid var(--border-light)",
            backgroundColor: "var(--bg-secondary)",
          }}
        >
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <span style={{ fontSize: "1.25rem" }}>⌨️</span>
            <div>
              <h2
                id="keyboard-shortcuts-title"
                style={{ margin: 0, fontSize: "1.05rem" }}
              >
                {t("modals.keyboardShortcuts.title", undefined, t("keyboardShortcuts.title", "Keyboard Shortcuts"))}
              </h2>
              <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                {t("keyboardShortcuts.subtitle", "Seedarr navigation and hotkeys")}
              </div>
            </div>
          </div>
          <button
            type="button"
            className="btn btn-sm btn-outline"
            onClick={onClose}
            aria-label={t("keyboardShortcuts.closeAria", "Close keyboard shortcuts dialog")}
            style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
          >
            ✕
          </button>
        </div>

        {/* Modal Body */}
        <div
          style={{
            overflowY: "auto",
            padding: "1.25rem",
            display: "flex",
            flexDirection: "column",
            gap: "1.25rem",
          }}
        >
          {groups.map((grp) => (
            <div key={grp.name}>
              <h3
                style={{
                  fontSize: "0.82rem",
                  textTransform: "uppercase",
                  letterSpacing: "0.05em",
                  color: "var(--accent, #c8a84e)",
                  margin: "0 0 0.6rem 0",
                  paddingBottom: "0.3rem",
                  borderBottom: "1px solid var(--border-light)",
                }}
              >
                {grp.name}
              </h3>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "1fr",
                  gap: "0.45rem",
                }}
              >
                {grp.shortcuts.map((sc, idx) => (
                  <div
                    key={idx}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      fontSize: "0.85rem",
                    }}
                  >
                    <span style={{ color: "var(--text-primary)" }}>
                      {sc.description}
                    </span>
                    <div
                      style={{
                        display: "flex",
                        gap: "0.3rem",
                        alignItems: "center",
                      }}
                    >
                      {sc.keys.map((k, kIdx) => (
                        <span key={kIdx} style={{ display: "inline-flex" }}>
                          <kbd
                            style={{
                              backgroundColor: "rgba(255, 255, 255, 0.08)",
                              border: "1px solid var(--border)",
                              borderRadius: "4px",
                              padding: "0.15rem 0.45rem",
                              fontSize: "0.75rem",
                              fontFamily: "monospace",
                              boxShadow: "0 1px 2px rgba(0,0,0,0.4)",
                              color: "var(--text-primary)",
                            }}
                          >
                            {k}
                          </kbd>
                          {kIdx < sc.keys.length - 1 && (
                            <span
                              style={{
                                color: "var(--text-dim)",
                                margin: "0 0.15rem",
                              }}
                            >
                              +
                            </span>
                          )}
                        </span>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>

        {/* Footer */}
        <div
          style={{
            padding: "0.75rem 1.25rem",
            backgroundColor: "var(--bg-secondary)",
            borderTop: "1px solid var(--border-light)",
            fontSize: "0.75rem",
            color: "var(--text-muted)",
            textAlign: "right",
          }}
        >
          {t("keyboardShortcuts.pressEscToClose", "Press ESC to close")}
        </div>
      </div>
    </div>
  );
}

export default KeyboardShortcutsModal;
