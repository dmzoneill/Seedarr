import { useState, useEffect } from "react";
import {
  useTorrents,
  useSeedingConfig,
  useSaveSeedingConfig,
} from "../../api/hooks";

export function QueueConcurrencyCard() {
  const { data: torrents } = useTorrents();
  const { data: config } = useSeedingConfig();
  const save = useSaveSeedingConfig();

  const [maxActiveDownloads, setMaxActiveDownloads] = useState<number>(5);
  const [maxActiveSeeds, setMaxActiveSeeds] = useState<number>(10);
  const [ignoreSlow, setIgnoreSlow] = useState<boolean>(true);

  useEffect(() => {
    if (config) {
      if (config.maxActiveDownloads !== undefined) {
        setMaxActiveDownloads(config.maxActiveDownloads);
      }
      if (config.maxActiveSeeds !== undefined) {
        setMaxActiveSeeds(config.maxActiveSeeds);
      }
      if (config.ignoreSlowTorrents !== undefined) {
        setIgnoreSlow(config.ignoreSlowTorrents);
      }
    }
  }, [config]);

  // Compute live active downloads and seeds
  const activeDownloads = (torrents ?? []).filter(
    (t) =>
      (t.status === "Downloading" || (t.downloadSpeed ?? 0) > 0) &&
      t.status !== "Stopped",
  ).length;

  const activeSeeds = (torrents ?? []).filter(
    (t) =>
      (t.status === "Seeding" || (t.uploadSpeed ?? 0) > 0) &&
      t.status !== "Stopped",
  ).length;

  const handleAdjustDownloads = (delta: number) => {
    const nextVal = Math.max(0, maxActiveDownloads + delta);
    setMaxActiveDownloads(nextVal);
    if (config) {
      save.mutate({
        ...config,
        maxActiveDownloads: nextVal,
      });
    }
  };

  const handleAdjustSeeds = (delta: number) => {
    const nextVal = Math.max(0, maxActiveSeeds + delta);
    setMaxActiveSeeds(nextVal);
    if (config) {
      save.mutate({
        ...config,
        maxActiveSeeds: nextVal,
      });
    }
  };

  const handleToggleIgnoreSlow = (checked: boolean) => {
    setIgnoreSlow(checked);
    if (config) {
      save.mutate({
        ...config,
        ignoreSlowTorrents: checked,
      });
    }
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
              onChange={(e) => handleToggleIgnoreSlow(e.target.checked)}
            />
            <span>Ignore slow / stalled transfers in queue</span>
          </label>
          <div className="quick-settings-subtext">
            Torrents transferring under 10 KB/s do not consume concurrency
            slots.
          </div>
        </div>
      </div>
    </div>
  );
}

export default QueueConcurrencyCard;
