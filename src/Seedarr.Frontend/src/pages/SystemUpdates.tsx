import { useState } from "react";
import { apiClient } from "../api/client";
import { useUpdates, useUpdateProgress, useInstallUpdate } from "../api/hooks";
import { useToast } from "../context/ToastContext";

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
  const { showToast } = useToast();
  const installUpdate = useInstallUpdate();

  const [showInstallModal, setShowInstallModal] = useState(false);
  const [targetVersion, setTargetVersion] = useState<string | null>(null);
  const [isRestarting, setIsRestarting] = useState(false);
  const [restartDone, setRestartDone] = useState(false);

  const { data: progress } = useUpdateProgress(
    showInstallModal || installUpdate.isPending
  );

  const isUpToDate =
    updates &&
    updates.length > 0 &&
    updates.every((u) => !u.latest || u.installed);

  const isContainerized = updates?.some((u) => u.isContainerized) ?? false;
  const latestUpdate = updates?.find((u) => u.latest && !u.installed);

  const handleStartInstall = (version?: string) => {
    const ver = version || latestUpdate?.version || undefined;
    setTargetVersion(ver ?? null);
    setShowInstallModal(true);
    setRestartDone(false);
    installUpdate.mutate(ver, {
      onError: (err: any) => {
        showToast(err?.message || "Failed to start update install", "error");
      },
    });
  };

  const handleRestart = async () => {
    setIsRestarting(true);
    try {
      await apiClient.post("/system/restart");
      setRestartDone(true);
      showToast("Seedarr is restarting. Please wait a few moments...", "info");
    } catch (err: any) {
      showToast(`Restart failed: ${err?.message || "Unknown error"}`, "error");
      setIsRestarting(false);
    }
  };

  const currentStage = progress?.stage ?? (installUpdate.isPending ? "Downloading" : "Idle");
  const currentPercentage = progress?.percentage ?? (installUpdate.isPending ? 10 : 0);
  const isRestartRequired =
    currentStage === "RestartRequired" ||
    currentStage?.toLowerCase() === "restartrequired";
  const isFailed =
    currentStage === "Failed" ||
    currentStage?.toLowerCase() === "failed" ||
    installUpdate.isError;
  const errorMessage =
    progress?.errorMessage ||
    (installUpdate.error as any)?.message ||
    null;

  const getStageDescription = () => {
    switch (currentStage?.toLowerCase()) {
      case "downloading":
        return "Downloading release package from GitHub...";
      case "verifying":
        return "Verifying package integrity with SHA-256 checksum...";
      case "extracting":
        return "Extracting update package contents...";
      case "installing":
        return "Safely staging binaries and replacing executables (ETXTBSY)...";
      case "restartrequired":
        return "Update installed successfully! A restart is required to run the new version.";
      case "failed":
        return errorMessage || "An error occurred during update installation.";
      default:
        return "Initializing update process...";
    }
  };

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
              justifyContent: "space-between",
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
              flexWrap: "wrap",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
              <span style={{ display: "flex", alignItems: "center" }}>
                <CheckIcon />
              </span>
              <div style={{ fontSize: "0.9rem", fontWeight: 600 }}>
                {isUpToDate
                  ? "The latest version of Seedarr is already installed"
                  : "A new version of Seedarr is available"}
              </div>
            </div>

            {!isUpToDate && !isContainerized && (
              <button
                className="btn btn-primary btn-small"
                onClick={() => handleStartInstall()}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.4rem",
                  fontWeight: 600,
                  cursor: "pointer",
                }}
              >
                <span>⚡</span> Install Update
              </button>
            )}
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
                    flexWrap: "wrap",
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

                  <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: "0.5rem" }}>
                    {update.installed && (
                      <span className="badge badge-seeding">
                        Currently Installed
                      </span>
                    )}
                    {update.latest && !update.installed && (
                      <span className="badge badge-queued">
                        Latest Release
                      </span>
                    )}
                    {!update.installed && !isContainerized && (
                      <button
                        className="btn btn-primary btn-small"
                        onClick={() => handleStartInstall(update.version)}
                        style={{
                          fontSize: "0.8rem",
                          padding: "0.25rem 0.6rem",
                          display: "flex",
                          alignItems: "center",
                          gap: "0.3rem",
                        }}
                      >
                        <span>⚡</span> Install
                      </button>
                    )}
                  </div>
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

          {/* Install Progress Modal */}
          {showInstallModal && (
            <div
              className="modal-backdrop"
              onClick={() => {
                if (isRestartRequired || isFailed) {
                  setShowInstallModal(false);
                }
              }}
            >
              <div
                className="modal"
                onClick={(e) => e.stopPropagation()}
                style={{
                  maxWidth: 520,
                  width: "90%",
                  borderRadius: "8px",
                  boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
                  border: "1px solid var(--border-light)",
                  padding: "1.5rem",
                }}
              >
                <h3
                  className="modal-title"
                  style={{
                    fontSize: "1.25rem",
                    marginBottom: "0.75rem",
                    display: "flex",
                    alignItems: "center",
                    gap: "0.5rem",
                  }}
                >
                  <span>📦</span> In-App Software Update
                </h3>

                <p
                  style={{
                    fontSize: "0.875rem",
                    color: "var(--text-secondary)",
                    marginBottom: "1rem",
                  }}
                >
                  {targetVersion ? `Installing version v${targetVersion}` : "Installing update..."}
                </p>

                {/* Progress Bar */}
                <div style={{ marginBottom: "1rem" }}>
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      fontSize: "0.85rem",
                      marginBottom: "0.35rem",
                      fontWeight: 600,
                    }}
                  >
                    <span>Stage: {currentStage}</span>
                    <span>{currentPercentage}%</span>
                  </div>

                  <div
                    style={{
                      width: "100%",
                      height: "12px",
                      borderRadius: "6px",
                      backgroundColor: "rgba(255, 255, 255, 0.1)",
                      overflow: "hidden",
                    }}
                  >
                    <div
                      style={{
                        width: `${Math.max(5, currentPercentage)}%`,
                        height: "100%",
                        backgroundColor: isFailed
                          ? "var(--danger, #dc3545)"
                          : isRestartRequired
                          ? "var(--success, #28a745)"
                          : "var(--accent, #c8a84e)",
                        transition: "width 0.3s ease",
                      }}
                    />
                  </div>
                </div>

                {/* Stage Description Alert */}
                <div
                  style={{
                    padding: "0.75rem 1rem",
                    borderRadius: "6px",
                    backgroundColor: isFailed
                      ? "rgba(220, 53, 69, 0.15)"
                      : isRestartRequired
                      ? "rgba(40, 167, 69, 0.15)"
                      : "rgba(200, 168, 78, 0.12)",
                    border: `1px solid ${
                      isFailed
                        ? "rgba(220, 53, 69, 0.35)"
                        : isRestartRequired
                        ? "rgba(40, 167, 69, 0.35)"
                        : "rgba(200, 168, 78, 0.35)"
                    }`,
                    color: isFailed
                      ? "var(--danger, #dc3545)"
                      : isRestartRequired
                      ? "var(--success, #28a745)"
                      : "var(--accent, #c8a84e)",
                    fontSize: "0.875rem",
                    marginBottom: "1.25rem",
                    lineHeight: 1.5,
                  }}
                >
                  {getStageDescription()}
                </div>

                {/* Modal Action Buttons */}
                <div
                  className="modal-actions"
                  style={{
                    display: "flex",
                    justifyContent: "flex-end",
                    gap: "0.5rem",
                  }}
                >
                  {isRestartRequired ? (
                    <button
                      className="btn btn-primary"
                      onClick={handleRestart}
                      disabled={isRestarting || restartDone}
                      style={{
                        fontWeight: 600,
                        display: "flex",
                        alignItems: "center",
                        gap: "0.4rem",
                      }}
                    >
                      <span>🔄</span>
                      {restartDone
                        ? "Restarting..."
                        : isRestarting
                        ? "Initiating Restart..."
                        : "Restart Seedarr Now"}
                    </button>
                  ) : isFailed ? (
                    <>
                      <button
                        className="btn btn-outline btn-small"
                        onClick={() => setShowInstallModal(false)}
                      >
                        Close
                      </button>
                      <button
                        className="btn btn-primary btn-small"
                        onClick={() => handleStartInstall(targetVersion ?? undefined)}
                      >
                        Retry
                      </button>
                    </>
                  ) : (
                    <button
                      className="btn btn-outline btn-small"
                      onClick={() => setShowInstallModal(false)}
                    >
                      Close (Runs in Background)
                    </button>
                  )}
                </div>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}

export default SystemUpdates;
