import { useState } from "react";
import { useHealthChecks } from "../api/hooks";

function HealthAlerts() {
  const { data: checks } = useHealthChecks();
  const [dismissed, setDismissed] = useState<string[]>(() => {
    try {
      return JSON.parse(localStorage.getItem("seedarr_dismissed_health_alerts") || "[]");
    } catch {
      return [];
    }
  });

  const handleDismiss = (source: string) => {
    setDismissed((prev) => {
      if (prev.includes(source)) return prev;
      const updated = [...prev, source];
      try {
        localStorage.setItem("seedarr_dismissed_health_alerts", JSON.stringify(updated));
      } catch {
        // ignore quota errors
      }
      return updated;
    });
  };

  const alerts = (checks ?? []).filter((c) => {
    if (dismissed.includes(c.source)) return false;
    // Handle both string (lowercase/PascalCase) and numeric enum from ASP.NET Core
    const isOk =
      c.type?.toLowerCase() === "ok" || (c.type as unknown) === 0;
    if (isOk) return false;
    if (!c.message || c.message.trim() === "") return false;
    return true;
  });

  if (alerts.length === 0) return null;

  return (
    <div className="health-alerts" style={{ padding: "0 0 1rem 0" }}>
      {alerts.map((alert) => {
        const typeStr =
          typeof alert.type === "string"
            ? alert.type.toLowerCase()
            : alert.type === 1
              ? "notice"
              : alert.type === 2
                ? "warning"
                : "error";

        const icon =
          typeStr === "error" ? "❌" : typeStr === "warning" ? "⚠️" : "ℹ️";

        return (
          <div
            key={alert.source}
            className={`health-alert health-alert-${typeStr}`}
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              gap: "0.75rem",
              padding: "0.6rem 1rem",
              borderRadius: "6px",
              marginBottom: "0.5rem",
              boxShadow: "0 2px 8px rgba(0, 0, 0, 0.25)",
            }}
          >
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
            >
              <span>{icon}</span>
              <span
                className="health-alert-message"
                style={{ fontWeight: 500 }}
              >
                {alert.message}
              </span>
            </div>
            <button
              className="health-alert-dismiss"
              onClick={() => handleDismiss(alert.source)}
              style={{
                cursor: "pointer",
                background: "none",
                border: "none",
                color: "inherit",
                fontSize: "0.9rem",
                opacity: 0.7,
                padding: "0.2rem 0.4rem",
              }}
              title="Dismiss alert"
            >
              ✕
            </button>
          </div>
        );
      })}
    </div>
  );
}

export default HealthAlerts;
