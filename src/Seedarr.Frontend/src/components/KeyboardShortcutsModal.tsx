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
  if (!isOpen) return null;

  const groups: ShortcutGroup[] = [
    {
      name: "Global Navigation",
      shortcuts: [
        {
          keys: ["Ctrl", "K"],
          description: "Open Command Palette / Quick Jump",
        },
        { keys: ["/"], description: "Focus Search / Quick Jump" },
        { keys: ["g", "d"], description: "Go to Dashboard" },
        { keys: ["g", "t"], description: "Go to Torrents Index" },
        { keys: ["g", "h"], description: "Go to Download History" },
        { keys: ["g", "b"], description: "Go to Tracker Boost" },
        { keys: ["g", "m"], description: "Go to Activity Metrics" },
        { keys: ["g", "p"], description: "Go to Peer Map" },
        { keys: ["g", "s"], description: "Go to Settings" },
      ],
    },
    {
      name: "Torrent Operations (Selected Torrent)",
      shortcuts: [
        { keys: ["Space"], description: "Pause / Resume Seeding" },
        { keys: ["p"], description: "Pause / Resume Seeding" },
        { keys: ["a"], description: "Force Announce to All Trackers" },
        { keys: ["r"], description: "Force Recheck Torrent Files" },
        { keys: ["Delete"], description: "Delete Torrent" },
      ],
    },
    {
      name: "Modals & Views",
      shortcuts: [
        {
          keys: ["?"],
          description: "Show this Keyboard Shortcuts cheat sheet",
        },
        { keys: ["Esc"], description: "Close active modal or detail panel" },
      ],
    },
  ];

  return (
    <div
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
          border: "1px solid rgba(255, 255, 255, 0.16)",
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
              <h2 style={{ margin: 0, fontSize: "1.05rem" }}>
                Keyboard Shortcuts
              </h2>
              <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                Seedarr navigation and hotkeys
              </div>
            </div>
          </div>
          <button
            className="btn btn-sm btn-outline"
            onClick={onClose}
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
                              border: "1px solid rgba(255, 255, 255, 0.2)",
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
          Press <kbd>ESC</kbd> to close
        </div>
      </div>
    </div>
  );
}

export default KeyboardShortcutsModal;
