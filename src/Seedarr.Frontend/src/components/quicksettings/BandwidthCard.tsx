import { useState, useEffect } from "react";
import { useSeedingConfig, useSaveSeedingConfig } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";
import type { SeedingConfig } from "../../api/types";
import { trackSpeedModeChange } from "../../utils/analytics";
import { useTranslation } from "../../i18n";
import { useToast } from "../../context/ToastContext";

export function BandwidthCard() {
  const { t } = useTranslation();
  const { showToast } = useToast();
  const { data: config, isLoading } = useSeedingConfig();
  const save = useSaveSeedingConfig();

  const [downloadLimit, setDownloadLimit] = useState<number>(1250);
  const [uploadLimit, setUploadLimit] = useState<number>(625);
  const [turtleMode, setTurtleMode] = useState<boolean>(false);

  useEffect(() => {
    if (config) {
      const isTurtle = config.alternativeSpeedEnabled ?? false;
      setTurtleMode(isTurtle);
      setDownloadLimit(
        isTurtle
          ? (config.altDownloadSpeedKbps ?? 0)
          : (config.maxDownloadSpeedKbps ?? 0),
      );
      setUploadLimit(
        isTurtle
          ? (config.altUploadSpeedKbps ?? 0)
          : (config.maxUploadSpeedKbps ?? 0),
      );
    }
  }, [config]);

  const handleUpdate = (updates: Partial<SeedingConfig>) => {
    if (!config) return;
    const newConfig: SeedingConfig = {
      ...config,
      ...updates,
    };
    if (turtleMode) {
      if (updates.altDownloadSpeedKbps !== undefined) {
        setDownloadLimit(updates.altDownloadSpeedKbps);
      }
      if (updates.altUploadSpeedKbps !== undefined) {
        setUploadLimit(updates.altUploadSpeedKbps);
      }
    } else {
      if (updates.maxDownloadSpeedKbps !== undefined) {
        setDownloadLimit(updates.maxDownloadSpeedKbps);
      }
      if (updates.maxUploadSpeedKbps !== undefined) {
        setUploadLimit(updates.maxUploadSpeedKbps);
      }
    }
    if (updates.alternativeSpeedEnabled !== undefined) {
      setTurtleMode(updates.alternativeSpeedEnabled);
    }
    save.mutate(newConfig, {
      onError: (err: any) => {
        showToast(
          t("quickSettings.failedToUpdateSpeed", { 0: err?.message || err }),
          "error",
        );
      },
    });
  };

  const handleToggleTurtle = () => {
    if (!config) return;
    const nextTurtle = !turtleMode;
    setTurtleMode(nextTurtle);
    const nextDl = nextTurtle
      ? (config.altDownloadSpeedKbps ?? 0)
      : (config.maxDownloadSpeedKbps ?? 0);
    const nextUl = nextTurtle
      ? (config.altUploadSpeedKbps ?? 0)
      : (config.maxUploadSpeedKbps ?? 0);
    setDownloadLimit(nextDl);
    setUploadLimit(nextUl);
    trackSpeedModeChange("alternative", nextTurtle);
    handleUpdate({ alternativeSpeedEnabled: nextTurtle });
  };

  const commitDownload = (val?: number) => {
    const valueToCommit = val !== undefined ? val : downloadLimit;
    if (val !== undefined) {
      setDownloadLimit(val);
    }
    const currentVal = turtleMode
      ? config?.altDownloadSpeedKbps
      : config?.maxDownloadSpeedKbps;
    if (currentVal === valueToCommit) {
      return;
    }
    if (turtleMode) {
      handleUpdate({ altDownloadSpeedKbps: valueToCommit });
    } else {
      handleUpdate({ maxDownloadSpeedKbps: valueToCommit });
    }
  };

  const commitUpload = (val?: number) => {
    const valueToCommit = val !== undefined ? val : uploadLimit;
    if (val !== undefined) {
      setUploadLimit(val);
    }
    const currentVal = turtleMode
      ? config?.altUploadSpeedKbps
      : config?.maxUploadSpeedKbps;
    if (currentVal === valueToCommit) {
      return;
    }
    if (turtleMode) {
      handleUpdate({ altUploadSpeedKbps: valueToCommit });
    } else {
      handleUpdate({ maxUploadSpeedKbps: valueToCommit });
    }
  };

  const handlePresetClick = (dl: number, ul: number) => {
    setDownloadLimit(dl);
    setUploadLimit(ul);
    if (turtleMode) {
      handleUpdate({
        altDownloadSpeedKbps: dl,
        altUploadSpeedKbps: ul,
      });
    } else {
      handleUpdate({
        maxDownloadSpeedKbps: dl,
        maxUploadSpeedKbps: ul,
      });
    }
  };

  const formatLimit = (kbps: number) => {
    if (kbps <= 0) return t("quickSettings.unlimited");
    return `${formatBytes(kbps * 1024)}/s`;
  };

  const presets = [
    { label: t("quickSettings.presetThrottled"), dl: 100, ul: 50 },
    { label: t("quickSettings.presetBalanced"), dl: 1250, ul: 625 },
    { label: t("quickSettings.presetHighSpeed"), dl: 10240, ul: 5120 },
    { label: t("quickSettings.presetUnlimited"), dl: 0, ul: 0 },
  ];

  if (isLoading) {
    return (
      <div className="quick-settings-card">
        <div className="quick-settings-card-header">
          <span className="quick-settings-card-title">
            {t("quickSettings.bandwidthLimits")}
          </span>
        </div>
        <div className="quick-settings-loading">
          {t("quickSettings.loadingBandwidth")}
        </div>
      </div>
    );
  }

  return (
    <div className="quick-settings-card">
      <div className="quick-settings-card-header">
        <div className="quick-settings-card-title-group">
          <span className="quick-settings-card-icon">⚡</span>
          <span className="quick-settings-card-title">
            {t("quickSettings.bandwidthLimits")}
          </span>
        </div>
        <button
          type="button"
          className={`quick-settings-turtle-btn ${turtleMode ? "active" : ""}`}
          onClick={handleToggleTurtle}
          title={t("quickSettings.turtleModeToggle")}
        >
          🐢{" "}
          {turtleMode
            ? t("quickSettings.turtleOn")
            : t("quickSettings.turtleMode")}
        </button>
      </div>

      <div className="quick-settings-card-body">
        {/* Download Speed Slider */}
        <div className="quick-settings-control-group">
          <div className="quick-settings-label-row">
            <span className="quick-settings-label">
              {t("quickSettings.maxDownload")}
            </span>
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
            onMouseUp={() => commitDownload()}
            onTouchEnd={() => commitDownload()}
            onKeyUp={() => commitDownload()}
            onBlur={() => commitDownload()}
            aria-label={t("quickSettings.maxDownload")}
          />
          <div className="quick-settings-slider-ticks">
            <span onClick={() => commitDownload(0)}>0 (∞)</span>
            <span onClick={() => commitDownload(1250)}>1.2M</span>
            <span onClick={() => commitDownload(10240)}>10M</span>
            <span onClick={() => commitDownload(50000)}>50M</span>
          </div>
        </div>

        {/* Upload Speed Slider */}
        <div className="quick-settings-control-group">
          <div className="quick-settings-label-row">
            <span className="quick-settings-label">
              {t("quickSettings.maxUpload")}
            </span>
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
            onMouseUp={() => commitUpload()}
            onTouchEnd={() => commitUpload()}
            onKeyUp={() => commitUpload()}
            onBlur={() => commitUpload()}
            aria-label={t("quickSettings.maxUpload")}
          />
          <div className="quick-settings-slider-ticks">
            <span onClick={() => commitUpload(0)}>0 (∞)</span>
            <span onClick={() => commitUpload(625)}>625K</span>
            <span onClick={() => commitUpload(5120)}>5M</span>
            <span onClick={() => commitUpload(50000)}>50M</span>
          </div>
        </div>

        {/* Presets */}
        <div className="quick-settings-presets-section">
          <span className="quick-settings-sublabel">
            {t("quickSettings.speedPresets")}
          </span>
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
                onClick={() => handlePresetClick(p.dl, p.ul)}
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
