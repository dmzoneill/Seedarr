import { useState, useEffect } from "react";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";

export function BandwidthCard() {
  const { data: config, isLoading } = useSeedingConfig();
  const save = useSaveSeedingConfig();

  const [downloadLimit, setDownloadLimit] = useState<number>(1250);
  const [uploadLimit, setUploadLimit] = useState<number>(625);
  const [turtleMode, setTurtleMode] = useState<boolean>(false);

  useEffect(() => {
    if (config) {
      setDownloadLimit(config.maxDownloadSpeedKbps ?? 0);
      setUploadLimit(config.maxUploadSpeedKbps ?? 0);
      setTurtleMode(config.alternativeSpeedEnabled ?? false);
    }
  }, [config]);

  const handleUpdate = (
    updates: Partial<{
      maxDownloadSpeedKbps: number;
      maxUploadSpeedKbps: number;
      alternativeSpeedEnabled: boolean;
    }>,
  ) => {
    if (!config) return;
    const newConfig = {
      ...config,
      ...updates,
    };
    if (updates.maxDownloadSpeedKbps !== undefined) {
      setDownloadLimit(updates.maxDownloadSpeedKbps);
    }
    if (updates.maxUploadSpeedKbps !== undefined) {
      setUploadLimit(updates.maxUploadSpeedKbps);
    }
    if (updates.alternativeSpeedEnabled !== undefined) {
      setTurtleMode(updates.alternativeSpeedEnabled);
    }
    save.mutate(newConfig);
  };

  const formatLimit = (kbps: number) => {
    if (kbps <= 0) return "∞ Unlimited";
    return `${formatBytes(kbps * 1024)}/s`;
  };

  const presets = [
    { label: "🐢 Throttled", dl: 100, ul: 50 },
    { label: "⚡ Balanced", dl: 1250, ul: 625 },
    { label: "🚀 High Speed", dl: 10240, ul: 5120 },
    { label: "∞ Unlimited", dl: 0, ul: 0 },
  ];

  if (isLoading) {
    return (
      <div className="quick-settings-card">
        <div className="quick-settings-card-header">
          <span className="quick-settings-card-title">Bandwidth Limits</span>
        </div>
        <div className="quick-settings-loading">Loading limits...</div>
      </div>
    );
  }

  return (
    <div className="quick-settings-card">
      <div className="quick-settings-card-header">
        <div className="quick-settings-card-title-group">
          <span className="quick-settings-card-icon">⚡</span>
          <span className="quick-settings-card-title">Bandwidth Limits</span>
        </div>
        <button
          type="button"
          className={`quick-settings-turtle-btn ${turtleMode ? "active" : ""}`}
          onClick={() => handleUpdate({ alternativeSpeedEnabled: !turtleMode })}
          title="Toggle Turtle Mode (Alternative Rate Limits)"
        >
          🐢 {turtleMode ? "Turtle ON" : "Turtle Mode"}
        </button>
      </div>

      <div className="quick-settings-card-body">
        {/* Download Speed Slider */}
        <div className="quick-settings-control-group">
          <div className="quick-settings-label-row">
            <span className="quick-settings-label">Max Download</span>
            <span className="quick-settings-val quick-settings-val-dl">
              {formatLimit(downloadLimit)}
            </span>
          </div>
          <input
            type="range"
            className="quick-settings-slider"
            min={0}
            max={50000}
            step={100}
            value={downloadLimit}
            onChange={(e) => setDownloadLimit(Number(e.target.value))}
            onMouseUp={() =>
              handleUpdate({ maxDownloadSpeedKbps: downloadLimit })
            }
            onTouchEnd={() =>
              handleUpdate({ maxDownloadSpeedKbps: downloadLimit })
            }
          />
          <div className="quick-settings-slider-ticks">
            <span onClick={() => handleUpdate({ maxDownloadSpeedKbps: 0 })}>
              0 (∞)
            </span>
            <span onClick={() => handleUpdate({ maxDownloadSpeedKbps: 1250 })}>
              1.2M
            </span>
            <span onClick={() => handleUpdate({ maxDownloadSpeedKbps: 10240 })}>
              10M
            </span>
            <span onClick={() => handleUpdate({ maxDownloadSpeedKbps: 50000 })}>
              50M
            </span>
          </div>
        </div>

        {/* Upload Speed Slider */}
        <div className="quick-settings-control-group">
          <div className="quick-settings-label-row">
            <span className="quick-settings-label">Max Upload</span>
            <span className="quick-settings-val quick-settings-val-ul">
              {formatLimit(uploadLimit)}
            </span>
          </div>
          <input
            type="range"
            className="quick-settings-slider"
            min={0}
            max={50000}
            step={50}
            value={uploadLimit}
            onChange={(e) => setUploadLimit(Number(e.target.value))}
            onMouseUp={() => handleUpdate({ maxUploadSpeedKbps: uploadLimit })}
            onTouchEnd={() => handleUpdate({ maxUploadSpeedKbps: uploadLimit })}
          />
          <div className="quick-settings-slider-ticks">
            <span onClick={() => handleUpdate({ maxUploadSpeedKbps: 0 })}>
              0 (∞)
            </span>
            <span onClick={() => handleUpdate({ maxUploadSpeedKbps: 625 })}>
              625K
            </span>
            <span onClick={() => handleUpdate({ maxUploadSpeedKbps: 5120 })}>
              5M
            </span>
            <span onClick={() => handleUpdate({ maxUploadSpeedKbps: 50000 })}>
              50M
            </span>
          </div>
        </div>

        {/* Presets */}
        <div className="quick-settings-presets-section">
          <span className="quick-settings-sublabel">Speed Presets:</span>
          <div className="quick-settings-chips">
            {presets.map((p) => (
              <button
                key={p.label}
                type="button"
                className={`quick-settings-chip ${
                  downloadLimit === p.dl && uploadLimit === p.ul
                    ? "selected"
                    : ""
                }`}
                onClick={() =>
                  handleUpdate({
                    maxDownloadSpeedKbps: p.dl,
                    maxUploadSpeedKbps: p.ul,
                  })
                }
              >
                {p.label}
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

export default BandwidthCard;
