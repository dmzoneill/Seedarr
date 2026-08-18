import { useUpdates } from "../api/hooks";

function CheckIcon() {
  return (
    <svg
      width="20"
      height="20"
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

  return (
    <div>
      <h1 className="page-heading">Updates</h1>

      {isLoading && <p className="loading">Checking for updates...</p>}

      {error && (
        <div className="card">
          <p className="error">Failed to check for updates.</p>
        </div>
      )}

      {updates && (
        <>
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              padding: "0.75rem 1rem",
              backgroundColor: "var(--bg-secondary)",
              borderBottom: "1px solid var(--border-light)",
            }}
          >
            {isUpToDate ? (
              <>
                <span style={{ color: "var(--success)", display: "flex" }}>
                  <CheckIcon />
                </span>
                <span
                  style={{
                    color: "var(--success)",
                    fontSize: "0.9rem",
                    fontWeight: 500,
                  }}
                >
                  The latest version of Seedarr is already installed
                </span>
              </>
            ) : (
              <span
                style={{
                  color: "var(--accent)",
                  fontSize: "0.9rem",
                  fontWeight: 500,
                }}
              >
                A new version of Seedarr is available
              </span>
            )}
          </div>

          {updates.map((update) => (
            <div
              key={update.version}
              className="card"
              style={{ padding: "1rem 1.5rem" }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.75rem",
                  marginBottom: "0.75rem",
                }}
              >
                <span
                  style={{
                    fontSize: "1rem",
                    fontWeight: 600,
                    color: "var(--text-primary)",
                  }}
                >
                  {update.version}
                </span>
                <span
                  style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}
                >
                  {formatDate(update.releaseDate)}
                </span>
                {update.installed && (
                  <span
                    className="badge badge-seeding"
                    style={{ marginLeft: "0.25rem" }}
                  >
                    Currently Installed
                  </span>
                )}
                {update.latest && !update.installed && (
                  <span
                    className="badge badge-queued"
                    style={{ marginLeft: "0.25rem" }}
                  >
                    Latest
                  </span>
                )}
              </div>

              {update.changes &&
                update.changes.new &&
                update.changes.new.length > 0 && (
                  <div style={{ marginBottom: "0.5rem" }}>
                    <div
                      style={{
                        fontSize: "0.75rem",
                        fontWeight: 600,
                        textTransform: "uppercase",
                        letterSpacing: "0.05em",
                        color: "var(--success)",
                        marginBottom: "0.3rem",
                      }}
                    >
                      New
                    </div>
                    <ul
                      style={{
                        margin: 0,
                        paddingLeft: "1.25rem",
                        fontSize: "0.85rem",
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
                        fontWeight: 600,
                        textTransform: "uppercase",
                        letterSpacing: "0.05em",
                        color: "var(--accent)",
                        marginBottom: "0.3rem",
                      }}
                    >
                      Fixed
                    </div>
                    <ul
                      style={{
                        margin: 0,
                        paddingLeft: "1.25rem",
                        fontSize: "0.85rem",
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
        </>
      )}
    </div>
  );
}

export default SystemUpdates;
