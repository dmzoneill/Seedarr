import { useState, useEffect } from "react";
import { useTorrents } from "../../api/hooks";

export function QueueConcurrencyCard() {
  const { data: torrents } = useTorrents();

  const [maxActiveDownloads, setMaxActiveDownloads] = useState<number>(() => {
    const saved = localStorage.getItem("seedarr_max_active_dl");
    return saved !== null ? Number(saved) : 5;
  });

  const [maxActiveSeeds, setMaxActiveSeeds] = useState<number>(() => {
    const saved = localStorage.getItem("seedarr_max_active_seeds");
    return saved !== null ? Number(saved) : 10;
  });

  const [ignoreSlow, setIgnoreSlow] = useState<boolean>(() => {
    const saved = localStorage.getItem("seedarr_ignore_slow_torrents");
    return saved !== null ? saved === "true" : true;
  });

  useEffect(() => {
    localStorage.setItem("seedarr_max_active_dl", String(maxActiveDownloads));
  }, [maxActiveDownloads]);

  useEffect(() => {
    localStorage.setItem("seedarr_max_active_seeds", String(maxActiveSeeds));
  }, [maxActiveSeeds]);

  useEffect(() => {
    localStorage.setItem("seedarr_ignore_slow_torrents", String(ignoreSlow));
  }, [ignoreSlow]);

  // Compute live active downloads and seeds
  const activeDownloads = (torrents ?? []).filter(
    (t) => (t.status === "Downloading" || (t.downloadSpeed ?? 0) > 0) && t.status !== "Stopped"
  ).length;

  const activeSeeds = (torrents ?? []).filter(
    (t) => (t.status === "Seeding" || (t.uploadSpeed ?? 0) > 0) && t.status !== "Stopped"
  ).length;

  const handleAdjustDownloads = (delta: number) => {
    setMaxActiveDownloads((prev) => Math.max(0, prev + delta));
  };

  const handleAdjustSeeds = (delta: number) => {
    setMaxActiveSeeds((prev) => Math.max(0, prev + delta));
  };

  return (
    <div className="quick-settings-card">
      <div className="quick-settings-card-header">
        <div className="quick-settings-card-title-group">
          <span className="quick-settings-card-icon">🎛️</span>
          <span className="quick-settings-card-title">Queue & Concurrency</span>
        </div>
        <span className="quick-settings-badge">
          Active: {activeDownloads + activeSeeds}
        </span>
      </div>

      <div className="quick-settings-card-body">
        {/* Max Active Downloads Stepper */}
        <div className="quick-settings-stepper-group">
          <div className="quick-settings-stepper-label-group">
            <span className="quick-settings-label">Max Active Downloads</span>
            <span className="quick-settings-stepper-stat">
              {activeDownloads} active
            </span>
          </div>
          <div className="quick-settings-stepper-controls">
            <button
              type="button"
              className="quick-settings-stepper-btn"
              onClick={() => handleAdjustDownloads(-1)}
              title="Decrease max downloads"
            >
              −
            </button>
            <span className="quick-settings-stepper-value">
              {maxActiveDownloads === 0 ? "∞ (Unlimited)" : maxActiveDownloads}
            </span>
            <button
              type="button"
              className="quick-settings-stepper-btn"
              onClick={() => handleAdjustDownloads(1)}
              title="Increase max downloads"
            >
              +
            </button>
          </div>
        </div>

        {/* Max Active Seeds Stepper */}
        <div className="quick-settings-stepper-group">
          <div className="quick-settings-stepper-label-group">
            <span className="quick-settings-label">Max Active Seeds</span>
            <span className="quick-settings-stepper-stat">
              {activeSeeds} active
            </span>
          </div>
          <div className="quick-settings-stepper-controls">
            <button
              type="button"
              className="quick-settings-stepper-btn"
              onClick={() => handleAdjustSeeds(-1)}
              title="Decrease max seeds"
            >
              −
            </button>
            <span className="quick-settings-stepper-value">
              {maxActiveSeeds === 0 ? "∞ (Unlimited)" : maxActiveSeeds}
            </span>
            <button
              type="button"
              className="quick-settings-stepper-btn"
              onClick={() => handleAdjustSeeds(1)}
              title="Increase max seeds"
            >
              +
            </button>
          </div>
        </div>

        {/* Ignore Slow Torrents Checkbox */}
        <div className="quick-settings-checkbox-group">
          <label className="quick-settings-checkbox-label">
            <input
              type="checkbox"
              className="quick-settings-checkbox"
              checked={ignoreSlow}
              onChange={(e) => setIgnoreSlow(e.target.checked)}
            />
            <span>Ignore slow / stalled transfers in queue</span>
          </label>
          <div className="quick-settings-subtext">
            Torrents transferring under 10 KB/s do not consume concurrency slots.
          </div>
        </div>
      </div>
    </div>
  );
}

export default QueueConcurrencyCard;
