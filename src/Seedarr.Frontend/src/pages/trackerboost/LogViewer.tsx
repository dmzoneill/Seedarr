import React, { useState, useMemo } from "react";
import { useTrackerBoostLogs, useClearTrackerBoostLogs } from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import TrackerFavicon from "../../components/TrackerFavicon";
import { useTranslation } from "../../i18n";

export function LogViewer() {
  const { t } = useTranslation();
  const { showToast } = useToast();

  const [logLevelFilter, setLogLevelFilter] = useState<string>("all");
  const [logCategoryFilter, setLogCategoryFilter] = useState<string>("all");
  const [logSearch, setLogSearch] = useState<string>("");
  const [logAutoRefresh, setLogAutoRefresh] = useState<boolean>(true);

  const {
    data: boostLogs,
    isLoading: logsLoading,
    refetch: refetchLogs,
  } = useTrackerBoostLogs(
    250,
    logCategoryFilter,
    logLevelFilter,
    logAutoRefresh ? 3000 : false,
  );
  const clearLogs = useClearTrackerBoostLogs();

  const handleClearLogs = () => {
    clearLogs.mutate(undefined, {
      onSuccess: () => {
        showToast(
          t(
            "trackerBoost.logs.logsClearedToast",
            undefined,
            "Activity logs cleared",
          ),
          "info",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "trackerBoost.logs.clearLogsFailed",
            { error: err.message },
            `Failed to clear logs: ${err.message}`,
          ),
          "error",
        );
      },
    });
  };

  const filteredLogs = useMemo(() => {
    return (boostLogs ?? []).filter((l) => {
      if (!logSearch.trim()) return true;
      const q = logSearch.toLowerCase();
      return (
        (l.message && l.message.toLowerCase().includes(q)) ||
        (l.trackerUrl && l.trackerUrl.toLowerCase().includes(q)) ||
        (l.infoHash && l.infoHash.toLowerCase().includes(q)) ||
        (l.category && l.category.toLowerCase().includes(q)) ||
        (l.level && l.level.toLowerCase().includes(q))
      );
    });
  }, [boostLogs, logSearch]);

  return (
    <div
      className="card"
      style={{
        padding: "1.25rem",
        flex: "1 1 auto",
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        marginBottom: "0.5rem",
      }}
    >
      {/* Controls bar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1rem",
          paddingBottom: "1rem",
          borderBottom: "1px solid var(--border-color, var(--border-light))",
        }}
      >
        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <select
            className="form-control"
            style={{
              width: "150px",
              padding: "0.4rem 0.6rem",
              fontSize: "0.82rem",
            }}
            value={logCategoryFilter}
            onChange={(e) => setLogCategoryFilter(e.target.value)}
          >
            <option value="all">
              {t("trackerBoost.allCategories", undefined, "All Categories")}
            </option>
            <option value="Scrape">
              {t("trackerBoost.logs.scrapes", undefined, "🔍 Scrapes")}
            </option>
            <option value="Health">
              {t(
                "trackerBoost.logs.healthProbes",
                undefined,
                "🩺 Health Probes",
              )}
            </option>
            <option value="Discovery">
              {t("trackerBoost.logs.discovery", undefined, "📡 Discovery")}
            </option>
            <option value="Inject">
              {t("trackerBoost.logs.injections", undefined, "⚡ Injections")}
            </option>
            <option value="Cycle">
              {t(
                "trackerBoost.logs.daemonCycles",
                undefined,
                "⚙️ Daemon Cycles",
              )}
            </option>
            <option value="General">
              {t("trackerBoost.general", undefined, "General")}
            </option>
          </select>

          <select
            className="form-control"
            style={{
              width: "130px",
              padding: "0.4rem 0.6rem",
              fontSize: "0.82rem",
            }}
            value={logLevelFilter}
            onChange={(e) => setLogLevelFilter(e.target.value)}
          >
            <option value="all">
              {t("trackerBoost.allLevels", undefined, "All Levels")}
            </option>
            <option value="Debug">
              {t("trackerBoost.debug", undefined, "Debug")}
            </option>
            <option value="Info">
              {t("trackerBoost.info", undefined, "Info")}
            </option>
            <option value="Warn">
              {t("trackerBoost.warn", undefined, "Warn")}
            </option>
            <option value="Error">
              {t("trackerBoost.error", undefined, "Error")}
            </option>
          </select>

          <input
            type="text"
            className="form-control"
            placeholder={t(
              "trackerBoost.logs.searchPlaceholder",
              undefined,
              "Search logs (message, url, hash)...",
            )}
            value={logSearch}
            onChange={(e) => setLogSearch(e.target.value)}
            style={{
              width: "280px",
              padding: "0.4rem 0.75rem",
              fontSize: "0.82rem",
            }}
          />
        </div>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              fontSize: "0.82rem",
              cursor: "pointer",
              userSelect: "none",
              marginRight: "0.5rem",
            }}
          >
            <input
              type="checkbox"
              checked={logAutoRefresh}
              onChange={(e) => setLogAutoRefresh(e.target.checked)}
            />
            <span>
              {t(
                "trackerBoost.logs.autoRefresh",
                undefined,
                "Auto-refresh (3s)",
              )}
            </span>
          </label>

          <button
            className="btn btn-outline"
            onClick={() => refetchLogs()}
            disabled={logsLoading}
            style={{ padding: "0.35rem 0.75rem", fontSize: "0.8rem" }}
          >
            {logsLoading ? "⏳" : "🔄"}{" "}
            {t("trackerBoost.logs.refreshBtn", undefined, "Refresh")}
          </button>

          <button
            className="btn btn-danger"
            onClick={handleClearLogs}
            disabled={
              clearLogs.isPending || !boostLogs || boostLogs.length === 0
            }
            style={{ padding: "0.35rem 0.75rem", fontSize: "0.8rem" }}
          >
            🗑️ {t("trackerBoost.logs.clearBtn", undefined, "Clear Logs")}
          </button>
        </div>
      </div>

      {/* Log Feed */}
      <div
        style={{
          flex: "1 1 auto",
          overflowY: "auto",
          fontFamily: "monospace",
          fontSize: "0.8rem",
          display: "flex",
          flexDirection: "column",
          gap: "4px",
          paddingRight: "0.5rem",
        }}
      >
        {filteredLogs.length === 0 ? (
          <div
            style={{
              textAlign: "center",
              padding: "3rem",
              color: "var(--text-muted)",
            }}
          >
            {logsLoading
              ? t("trackerBoost.logs.loadingLogs", undefined, "Loading logs...")
              : t(
                  "trackerBoost.logs.noLogsMatch",
                  undefined,
                  "No logs match the current filters.",
                )}
          </div>
        ) : (
          filteredLogs.map((log) => {
            let badgeBg = "rgba(255, 255, 255, 0.1)";
            let badgeColor = "#ccc";
            if (log.level === "Error") {
              badgeBg = "rgba(239, 68, 68, 0.2)";
              badgeColor = "#ef4444";
            } else if (log.level === "Warn") {
              badgeBg = "rgba(245, 158, 11, 0.2)";
              badgeColor = "#f59e0b";
            } else if (log.level === "Info") {
              badgeBg = "rgba(59, 130, 246, 0.2)";
              badgeColor = "#3b82f6";
            }

            let catColor = "#888";
            if (log.category === "Inject") catColor = "#10b981";
            else if (log.category === "Scrape") catColor = "#8b5cf6";
            else if (log.category === "Health") catColor = "#06b6d4";
            else if (log.category === "Discovery") catColor = "#ec4899";

            return (
              <div
                key={log.id}
                style={{
                  display: "flex",
                  alignItems: "flex-start",
                  gap: "0.6rem",
                  padding: "0.35rem 0.6rem",
                  borderRadius: "4px",
                  background:
                    log.level === "Error"
                      ? "rgba(239, 68, 68, 0.05)"
                      : "rgba(255, 255, 255, 0.02)",
                  borderLeft: `3px solid ${
                    log.level === "Error"
                      ? "#ef4444"
                      : log.level === "Warn"
                        ? "#f59e0b"
                        : "transparent"
                  }`,
                }}
              >
                <span
                  style={{
                    color: "var(--text-muted)",
                    flexShrink: 0,
                    fontSize: "0.75rem",
                    paddingTop: "2px",
                  }}
                >
                  {new Date(log.timestamp).toLocaleTimeString()}
                </span>

                <span
                  style={{
                    padding: "1px 5px",
                    borderRadius: "3px",
                    fontSize: "0.72rem",
                    fontWeight: "bold",
                    background: badgeBg,
                    color: badgeColor,
                    flexShrink: 0,
                  }}
                >
                  {log.level.toUpperCase()}
                </span>

                <span
                  style={{
                    padding: "1px 5px",
                    borderRadius: "3px",
                    fontSize: "0.72rem",
                    fontWeight: 600,
                    background: "rgba(255, 255, 255, 0.05)",
                    color: catColor,
                    flexShrink: 0,
                  }}
                >
                  {log.category}
                </span>

                <div
                  style={{
                    flex: "1 1 auto",
                    wordBreak: "break-word",
                    lineHeight: 1.4,
                  }}
                >
                  {log.trackerUrl && (
                    <span
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "4px",
                        marginRight: "6px",
                      }}
                    >
                      <TrackerFavicon urlOrHost={log.trackerUrl} size={12} />
                      <span
                        style={{
                          color: "var(--text-muted)",
                          fontSize: "0.75rem",
                        }}
                      >
                        [{log.trackerUrl}]
                      </span>
                    </span>
                  )}
                  <span>{log.message}</span>
                  {log.infoHash && (
                    <span
                      style={{
                        marginLeft: "6px",
                        color: "var(--text-muted)",
                        fontSize: "0.72rem",
                        fontFamily: "monospace",
                      }}
                    >
                      (hash: {log.infoHash.substring(0, 10)}...)
                    </span>
                  )}
                </div>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
