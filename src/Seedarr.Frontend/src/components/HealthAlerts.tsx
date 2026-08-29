import { useState } from "react";
import { useHealthChecks } from "../api/hooks";

function HealthAlerts() {
  const { data: checks } = useHealthChecks();
  const [dismissed, setDismissed] = useState<string[]>([]);

  const alerts = (checks ?? []).filter(
    (c) => c.type !== "Ok" && !dismissed.includes(c.source),
  );

  if (alerts.length === 0) return null;

  return (
    <div className="health-alerts">
      {alerts.map((alert) => (
        <div
          key={alert.source}
          className={`health-alert health-alert-${alert.type.toLowerCase()}`}
        >
          <span className="health-alert-message">{alert.message}</span>
          <button
            className="health-alert-dismiss"
            onClick={() => setDismissed((d) => [...d, alert.source])}
          >
            x
          </button>
        </div>
      ))}
    </div>
  );
}

export default HealthAlerts;
