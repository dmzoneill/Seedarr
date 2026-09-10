import { useState, useEffect } from "react";
import {
  useSeedingConfig,
  useSaveSeedingConfig,
  useDiskSpace,
} from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";

export function SeedingAutomationCard() {
  const { data: config, isLoading } = useSeedingConfig();
  const save = useSaveSeedingConfig();
  const { data: diskList } = useDiskSpace();

  const [ratioLimit, setRatioLimit] = useState<number>(0);
  const [sequentialDefault, setSequentialDefault] = useState<boolean>(() => {
    const saved = localStorage.getItem("seedarr_sequential_default");
    return saved !== null ? saved === "true" : false;
  });

  useEffect(() => {
    if (config) {
      setRatioLimit(config.globalSeedRatioLimit ?? 0);
    }
  }, [config]);

  useEffect(() => {
    localStorage.setItem(
      "seedarr_sequential_default",
      String(sequentialDefault),
    );
  }, [sequentialDefault]);

  const handleRatioChange = (val: number) => {
    if (!config) return;
    setRatioLimit(val);
    save.mutate({
      ...config,
      globalSeedRatioLimit: val,
    });
  };

  const ratioPresets = [
    { label: "1.0x", value: 1.0 },
    { label: "1.5x", value: 1.5 },
    { label: "2.0x", value: 2.0 },
    { label: "∞ (No limit)", value: 0 },
  ];

  // Pick primary disk space
  const primaryDisk = diskList && diskList.length > 0 ? diskList[0] : null;
  const usedSpace = primaryDisk
    ? primaryDisk.totalSpace - primaryDisk.freeSpace
    : 0;
  const usedPercent =
    primaryDisk && primaryDisk.totalSpace > 0
      ? Math.round((usedSpace / primaryDisk.totalSpace) * 100)
      : 0;

  if (isLoading) {
    return (
      <div className="quick-settings-card">
        <div className="quick-settings-card-header">
          <span className="quick-settings-card-title">Seeding & Storage</span>
        </div>
        <div className="quick-settings-loading">
          Loading seeding settings...
        </div>
      </div>
    );
  }

  return (
    <div className="quick-settings-card">
      <div className="quick-settings-card-header">
        <div className="quick-settings-card-title-group">
          <span className="quick-settings-card-icon">🌱</span>
          <span className="quick-settings-card-title">Seeding & Storage</span>
        </div>
        <span className="quick-settings-badge">
          Goal: {ratioLimit === 0 ? "∞ Unlimited" : `${ratioLimit.toFixed(1)}x`}
        </span>
      </div>

      <div className="quick-settings-card-body">
        {/* Global Target Ratio */}
        <div className="quick-settings-control-group">
          <div className="quick-settings-label-row">
            <span className="quick-settings-label">Target Ratio Goal</span>
            <span className="quick-settings-val quick-settings-val-ratio">
              {ratioLimit === 0 ? "∞ Unlimited" : `${ratioLimit.toFixed(1)}x`}
            </span>
          </div>
          <div className="quick-settings-chips">
            {ratioPresets.map((rp) => (
              <button
                key={rp.label}
                type="button"
                className={`quick-settings-chip ${
                  ratioLimit === rp.value ? "selected" : ""
                }`}
                onClick={() => handleRatioChange(rp.value)}
              >
                {rp.label}
              </button>
            ))}
          </div>
        </div>

        {/* Sequential Download by Default Checkbox */}
        <div className="quick-settings-checkbox-group">
          <label className="quick-settings-checkbox-label">
            <input
              type="checkbox"
              className="quick-settings-checkbox"
              checked={sequentialDefault}
              onChange={(e) => setSequentialDefault(e.target.checked)}
            />
            <span>Sequential piece picking by default</span>
          </label>
          <div className="quick-settings-subtext">
            Enables instant media inspection & streamable progressive previews.
          </div>
        </div>

        {/* Live Disk Storage Progress Bar */}
        <div className="quick-settings-disk-section">
          <div className="quick-settings-disk-label-row">
            <span className="quick-settings-label">
              Disk: {primaryDisk?.path || "/"}
            </span>
            <span className="quick-settings-disk-stat">
              {primaryDisk
                ? `${formatBytes(primaryDisk.freeSpace)} free (${usedPercent}% used)`
                : "Checking storage..."}
            </span>
          </div>
          <div className="quick-settings-disk-bar-bg">
            <div
              className={`quick-settings-disk-bar-fill ${
                usedPercent > 90
                  ? "danger"
                  : usedPercent > 75
                    ? "warning"
                    : "normal"
              }`}
              style={{ width: `${Math.min(100, Math.max(0, usedPercent))}%` }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

export default SeedingAutomationCard;
