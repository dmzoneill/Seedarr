import { useUpdates } from "../api/hooks";

function CheckIcon() {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.5"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="20 6 9 17 4 12" />
    </svg>
  );
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: "numeric",
    month: "long",
    day: "numeric",
  });
}

function SystemUpdates() {
  const { data: updates, isLoading, error } = useUpdates();

  const isUpToDate =
    updates &&
    updates.length > 0 &&
    updates.every((u) => !u.latest || u.installed);

  const isContainerized = updates?.some((u) => u.isContainerized) ?? false;

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>🔄</span> System: Updates
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Software version history, changelogs, bug fixes, and upgrade availability
          </p>
        </div>
      </div>

      {isLoading && <p className="loading">Checking for updates...</p>}

      {error && (
        <div className="card" style={{ marginBottom: "1rem" }}>
          <p className="error">Failed to check for updates.</p>
        </div>
      )}

      {updates && (
        <>
          {/* Container Environment Alert Banner */}
          {isContainerized && (
            <div
              className="card"
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
                padding: "1rem 1.25rem",
                marginBottom: "1.25rem",
                borderRadius: "8px",
                backgroundColor: "rgba(0, 123, 255, 0.12)",
                border: "1px solid rgba(0, 123, 255, 0.35)",
                color: "var(--info, #17a2b8)",
              }}
            >
              <span style={{ display: "flex", alignItems: "center", fontSize: "1.2rem" }}>
                🐳
              </span>
              <div style={{ fontSize: "0.9rem" }}>
                Seedarr is running inside a Docker container. Update your deployment by pulling the latest image:{" "}
                <code
                  style={{
                    backgroundColor: "rgba(0, 0, 0, 0.25)",
                    padding: "0.2rem 0.4rem",
                    borderRadius: "4px",
                    fontFamily: "monospace",
                    fontSize: "0.85rem",
                  }}
                >
                  docker compose pull &amp;&amp; docker compose up -d
                </code>
              </div>
            </div>
          )}

          {/* Status Alert Banner */}
          <div
            className="card"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              padding: "1rem 1.25rem",
              marginBottom: "1.25rem",
              borderRadius: "8px",
              backgroundColor: isUpToDate
                ? "rgba(40, 167, 69, 0.12)"
                : "rgba(200, 168, 78, 0.12)",
              border: `1px solid ${
                isUpToDate
                  ? "rgba(40, 167, 69, 0.35)"
                  : "rgba(200, 168, 78, 0.35)"
              }`,
              color: isUpToDate
                ? "var(--success, #28a745)"
                : "var(--accent, #c8a84e)",
            }}
          >
            <span style={{ display: "flex", alignItems: "center" }}>
              <CheckIcon />
            </span>
            <div style={{ fontSize: "0.9rem", fontWeight: 600 }}>
              {isUpToDate
                ? "The latest version of Seedarr is already installed"
                : "A new version of Seedarr is available"}
            </div>
          </div>

          {/* Release History Cards */}
          <div
            style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
          >
            {updates.map((update) => (
              <div
                key={update.version}
                className="card"
                style={{
                  padding: "1.25rem 1.5rem",
                  borderRadius: "8px",
                  border: "1px solid var(--border-light)",
                  boxShadow:
                    "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.75rem",
                    marginBottom: "1rem",
                    borderBottom: "1px solid var(--border-light)",
                    paddingBottom: "0.75rem",
                  }}
                >
                  <span
                    style={{
                      fontSize: "1.1rem",
                      fontWeight: 700,
                      color: "var(--accent, #c8a84e)",
                    }}
                  >
                    v{update.version}
                  </span>
                  <span
                    style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}
                  >
                    📅 {formatDate(update.releaseDate)}
                  </span>
                  {update.installed && (
                    <span
                      className="badge badge-seeding"
                      style={{ marginLeft: "auto" }}
                    >
                      Currently Installed
                    </span>
                  )}
                  {update.latest && !update.installed && (
                    <span
                      className="badge badge-queued"
                      style={{ marginLeft: "auto" }}
                    >
                      Latest Release
                    </span>
                  )}
                </div>

                {update.changes &&
                  update.changes.new &&
                  update.changes.new.length > 0 && (
                    <div style={{ marginBottom: "0.85rem" }}>
                      <div
                        style={{
                          fontSize: "0.75rem",
                          fontWeight: 700,
                          textTransform: "uppercase",
                          letterSpacing: "0.05em",
                          color: "var(--success, #28a745)",
                          marginBottom: "0.4rem",
                        }}
                      >
                        ✨ New Features
                      </div>
                      <ul
                        style={{
                          margin: 0,
                          paddingLeft: "1.25rem",
                          fontSize: "0.875rem",
                          color: "var(--text-secondary)",
                          lineHeight: 1.6,
                        }}
                      >
                        {update.changes.new.map((item, i) => (
                          <li key={i}>{item}</li>
                        ))}
                      </ul>
                    </div>
                  )}

                {update.changes &&
                  update.changes.fixed &&
                  update.changes.fixed.length > 0 && (
                    <div>
                      <div
                        style={{
                          fontSize: "0.75rem",
                          fontWeight: 700,
                          textTransform: "uppercase",
                          letterSpacing: "0.05em",
                          color: "var(--accent, #c8a84e)",
                          marginBottom: "0.4rem",
                        }}
                      >
                        🛠️ Bug Fixes & Improvements
                      </div>
                      <ul
                        style={{
                          margin: 0,
                          paddingLeft: "1.25rem",
                          fontSize: "0.875rem",
                          color: "var(--text-secondary)",
                          lineHeight: 1.6,
                        }}
                      >
                        {update.changes.fixed.map((item, i) => (
                          <li key={i}>{item}</li>
                        ))}
                      </ul>
                    </div>
                  )}
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

export default SystemUpdates;
