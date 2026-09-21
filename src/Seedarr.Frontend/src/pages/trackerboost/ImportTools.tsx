import React, { useState, useMemo } from "react";
import { useTranslation } from "../../i18n";
import {
  useTrackerBoostSettings,
  useUpdateTrackerBoostSettings,
  useDownloadClients,
  useHarvestDownloadTrackers,
  useHarvestProwlarrTrackers,
  useHarvestFeedTrackers,
  useScanTrackerBoostTrackers,
  useAddTrackerBoostTracker,
} from "../../api/hooks";
import { useToast } from "../../context/ToastContext";
import type { TrackerBoostSettings } from "../../api/types";

export interface ImportToolsProps {
  showModal?: boolean;
  onCloseModal?: () => void;
}

export interface BulkImportModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export function BulkImportModal({ isOpen, onClose }: BulkImportModalProps) {
  const { t } = useTranslation();
  const addTracker = useAddTrackerBoostTracker();
  const { showToast } = useToast();
  const [bulkImportText, setBulkImportText] = useState("");
  const [isBulkImporting, setIsBulkImporting] = useState(false);

  if (!isOpen) return null;

  const handleBulkImportTrackers = async () => {
    if (!bulkImportText.trim()) return;
    const lines = bulkImportText
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter(
        (l) =>
          l.startsWith("http://") ||
          l.startsWith("https://") ||
          l.startsWith("udp://"),
      );

    if (lines.length === 0) {
      showToast(
        t(
          "trackerBoost.settings.noValidUrlsFound",
          undefined,
          "No valid http://, https://, or udp:// tracker URLs found.",
        ),
        "error",
      );
      return;
    }

    setIsBulkImporting(true);
    let imported = 0;
    for (const url of lines) {
      try {
        await addTracker.mutateAsync({ url });
        imported++;
      } catch {
        // Continue on duplicates/errors
      }
    }
    setIsBulkImporting(false);
    onClose();
    setBulkImportText("");
    showToast(
      t(
        "trackerBoost.settings.processedTrackersToast",
        { total: lines.length, imported },
        `Successfully processed ${lines.length} trackers (${imported} added)!`,
      ),
      "success",
    );
  };

  return (
    <div
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.75)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
        padding: "1rem",
      }}
    >
      <div
        className="card"
        style={{
          width: "100%",
          maxWidth: "560px",
          padding: "1.5rem",
          borderRadius: "8px",
          display: "flex",
          flexDirection: "column",
          gap: "1rem",
          backgroundColor: "var(--bg-card)",
          boxShadow: "0 8px 32px rgba(0,0,0,0.5)",
          border: "1px solid var(--border-color, var(--border))",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
          }}
        >
          <h3 style={{ margin: 0 }}>
            {t(
              "trackerBoost.settings.bulkImportModalTitle",
              undefined,
              "📥 Bulk Import Tracker URLs",
            )}
          </h3>
          <button
            type="button"
            className="btn btn-outline"
            style={{ padding: "0.2rem 0.5rem" }}
            onClick={onClose}
          >
            ✕
          </button>
        </div>

        <p
          style={{
            fontSize: "0.85rem",
            color: "var(--text-muted)",
            margin: 0,
          }}
        >
          {t(
            "trackerBoost.settings.bulkImportModalHint",
            undefined,
            "Paste tracker announce URLs (one per line). Supported protocols: udp://, http://, https://.",
          )}
        </p>

        <textarea
          className="form-control"
          rows={8}
          placeholder="udp://tracker.opentrackr.org:1337/announce&#10;http://tracker.example.com/announce&#10;udp://open.stealth.si:80/announce"
          value={bulkImportText}
          onChange={(e) => setBulkImportText(e.target.value)}
          style={{ fontFamily: "monospace", fontSize: "0.82rem" }}
        />

        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            gap: "0.5rem",
          }}
        >
          <button type="button" className="btn btn-action" onClick={onClose}>
            {t("common.cancel", undefined, "Cancel")}
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleBulkImportTrackers}
            disabled={isBulkImporting || !bulkImportText.trim()}
          >
            {isBulkImporting
              ? t(
                  "trackerBoost.settings.importingTrackers",
                  undefined,
                  "Importing Trackers...",
                )
              : t("trackerBoost.settings.importTrackersBtn", undefined, "Import Trackers")}
          </button>
        </div>
      </div>
    </div>
  );
}

export function ImportTools({ showModal, onCloseModal }: ImportToolsProps) {
  const { t } = useTranslation();
  const { data: settings } = useTrackerBoostSettings();
  const updateSettings = useUpdateTrackerBoostSettings();
  const { data: downloadClients } = useDownloadClients();
  const { showToast } = useToast();

  const harvestDownloads = useHarvestDownloadTrackers();
  const harvestProwlarr = useHarvestProwlarrTrackers();
  const harvestFeeds = useHarvestFeedTrackers();
  const scanTrackers = useScanTrackerBoostTrackers();

  const [localShowModal, setLocalShowModal] = useState(false);

  const isModalOpen = showModal ?? localShowModal;
  const handleClose = onCloseModal ?? (() => setLocalShowModal(false));

  const handleToggleSetting = (key: keyof TrackerBoostSettings) => {
    if (!settings) return;
    const updated = { ...settings, [key]: !settings[key] };
    updateSettings.mutate(updated, {
      onSuccess: () => {
        showToast(
          t(
            "trackerBoost.settings.updatedToast",
            undefined,
            "TrackerBoost settings updated",
          ),
          "success",
        );
      },
    });
  };

  const handleHarvestDownloads = () => {
    harvestDownloads.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          t(
            "trackerBoost.harvestedTrackersSuccess",
            { count: res.harvestedCount },
            `Harvested ${res.harvestedCount} new trackers from active downloads`,
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "trackerBoost.harvestFailed",
            { error: err.message },
            `Failed to harvest from downloads: ${err.message}`,
          ),
          "error",
        );
      },
    });
  };

  const handleHarvestProwlarr = () => {
    harvestProwlarr.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          t(
            "trackerBoost.settings.harvestedProwlarrToast",
            { count: res.harvestedCount },
            `Harvested ${res.harvestedCount} trackers from Prowlarr`,
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "trackerBoost.settings.harvestProwlarrFailed",
            { error: err.message },
            `Failed to harvest from Prowlarr: ${err.message}`,
          ),
          "error",
        );
      },
    });
  };

  const handleHarvestFeeds = () => {
    harvestFeeds.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          t(
            "trackerBoost.settings.harvestedFeedsToast",
            { count: res.harvestedCount },
            `Harvested ${res.harvestedCount} trackers from public feeds`,
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "trackerBoost.settings.harvestFeedsFailed",
            { error: err.message },
            `Failed to harvest from feeds: ${err.message}`,
          ),
          "error",
        );
      },
    });
  };

  const handleScanAll = () => {
    scanTrackers.mutate(undefined, {
      onSuccess: (res) => {
        showToast(
          t(
            "trackerBoost.probedEndpointsSuccess",
            { count: res.testedCount },
            `Probed ${res.testedCount} tracker endpoints`,
          ),
          "success",
        );
      },
      onError: (err) => {
        showToast(
          t(
            "trackerBoost.probeTrackersFailed",
            { error: err.message },
            `Failed to probe trackers: ${err.message}`,
          ),
          "error",
        );
      },
    });
  };

  const enabledClientsCount = useMemo(() => {
    return (downloadClients ?? []).filter((c) => c.enable).length;
  }, [downloadClients]);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      {/* Automation Toggles */}
      <div className="card" style={{ padding: "1.25rem" }}>
        <h3 style={{ margin: "0 0 0.5rem 0" }}>
          {t(
            "trackerBoost.settings.automationTitle",
            undefined,
            "⚡ Automation & Background Optimization",
          )}
        </h3>
        <p
          style={{
            fontSize: "0.85rem",
            color: "var(--text-muted)",
            margin: "0 0 1rem 0",
          }}
        >
          {t(
            "trackerBoost.settings.automationDesc",
            undefined,
            "TrackerBoost runs as a background service to constantly discover new trackers, monitor health, and optimize swarms across Seedarr and connected download clients.",
          )}
        </p>

        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "0.75rem",
          }}
        >
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={settings?.autoBoostEnabled ?? true}
              onChange={() => handleToggleSetting("autoBoostEnabled")}
              style={{ width: "1.1rem", height: "1.1rem" }}
            />
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                {t(
                  "trackerBoost.settings.autoBoostLabel",
                  undefined,
                  "Automatic Background Swarm Boosting (Enabled by Default)",
                )}
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                {t(
                  "trackerBoost.settings.autoBoostDesc",
                  undefined,
                  "Periodically queries candidate trackers and automatically injects verified positive matches into active downloads.",
                )}
              </div>
            </div>
          </label>

          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={settings?.autoHarvestEnabled ?? true}
              onChange={() => handleToggleSetting("autoHarvestEnabled")}
              style={{ width: "1.1rem", height: "1.1rem" }}
            />
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                {t(
                  "trackerBoost.settings.autoHarvestLabel",
                  undefined,
                  "Automatic Swarm Tracker Harvesting (Enabled by Default)",
                )}
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                {t(
                  "trackerBoost.settings.autoHarvestDesc",
                  undefined,
                  "Continuously extracts and catalogues new public tracker endpoints from downloading torrents to grow the tracker database.",
                )}
              </div>
            </div>
          </label>

          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.75rem",
              cursor: "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={settings?.onlyVerified ?? true}
              onChange={() => handleToggleSetting("onlyVerified")}
              style={{ width: "1.1rem", height: "1.1rem" }}
            />
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>
                {t(
                  "trackerBoost.settings.onlyVerifiedLabel",
                  undefined,
                  "Scrape Verification Guard (Strict Mode)",
                )}
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                {t(
                  "trackerBoost.settings.onlyVerifiedDesc",
                  undefined,
                  "Only injects trackers that respond with active seeders or leechers for the specific info_hash, preventing client clutter.",
                )}
              </div>
            </div>
          </label>
        </div>
      </div>

      {/* Connected Download Agents */}
      <div className="card" style={{ padding: "1.25rem" }}>
        <h3 style={{ margin: "0 0 0.5rem 0" }}>
          {t(
            "trackerBoost.settings.connectedClientsTitle",
            undefined,
            "Connected Download Agents",
          )}
        </h3>
        <p
          style={{
            fontSize: "0.85rem",
            color: "var(--text-muted)",
            margin: "0 0 1rem 0",
          }}
        >
          {t(
            "trackerBoost.settings.connectedClientsDesc",
            undefined,
            "TrackerBoost coordinates with your download clients (qBittorrent, Transmission, Deluge) to inject verified trackers into active physical downloads.",
          )}
        </p>
        <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
          {(downloadClients ?? [])
            .filter((c) => c.enable)
            .map((client) => (
              <span
                key={client.id}
                className="badge badge-primary"
                style={{ padding: "0.4rem 0.75rem", fontSize: "0.85rem" }}
              >
                ⚡ {client.name} ({client.clientType})
              </span>
            ))}
          {enabledClientsCount === 0 && (
            <span style={{ fontSize: "0.85rem", color: "var(--warning, #f59e0b)" }}>
              {t(
                "trackerBoost.settings.noClientsWarning",
                undefined,
                "No download agents currently configured. Add qBittorrent or Transmission in Settings ⚙️ to boost real downloads.",
              )}
            </span>
          )}
        </div>
      </div>

      {/* Discovery Feeds */}
      <div className="card" style={{ padding: "1.25rem" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: "0.5rem",
          }}
        >
          <h3 style={{ margin: "0" }}>
            {t(
              "trackerBoost.settings.manualDiscoveryTitle",
              undefined,
              "Manual Discovery & Import Triggers",
            )}
          </h3>
          <button
            className="btn btn-primary"
            onClick={() => setLocalShowModal(true)}
          >
            {t(
              "trackerBoost.settings.bulkImportTrackersBtn",
              undefined,
              "📥 Bulk Import Trackers",
            )}
          </button>
        </div>
        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            flexWrap: "wrap",
            marginTop: "0.75rem",
          }}
        >
          <button
            className="btn btn-action"
            onClick={handleHarvestDownloads}
            disabled={harvestDownloads.isPending}
          >
            {harvestDownloads.isPending
              ? t(
                  "trackerBoost.settings.harvestingSwarms",
                  undefined,
                  "⏳ Harvesting Swarms...",
                )
              : t(
                  "trackerBoost.settings.harvestLiveSwarms",
                  undefined,
                  "🔄 Harvest Live Swarms",
                )}
          </button>
          <button
            className="btn btn-action"
            onClick={handleHarvestProwlarr}
            disabled={harvestProwlarr.isPending}
          >
            {harvestProwlarr.isPending
              ? t(
                  "trackerBoost.settings.syncingProwlarr",
                  undefined,
                  "⏳ Syncing Prowlarr...",
                )
              : t(
                  "trackerBoost.settings.syncProwlarr",
                  undefined,
                  "🔄 Sync Prowlarr Trackers",
                )}
          </button>
          <button
            className="btn btn-action"
            onClick={handleHarvestFeeds}
            disabled={harvestFeeds.isPending}
          >
            {harvestFeeds.isPending
              ? t("trackerBoost.settings.syncingFeeds", undefined, "⏳ Syncing Feeds...")
              : t("trackerBoost.settings.syncFeeds", undefined, "🌐 Sync Curated Feeds")}
          </button>
          <button
            className="btn btn-action"
            onClick={handleScanAll}
            disabled={scanTrackers.isPending}
          >
            {scanTrackers.isPending
              ? t(
                  "trackerBoost.settings.probingTrackers",
                  undefined,
                  "⏳ Probing Trackers...",
                )
              : t("trackerBoost.probeAllTrackers", undefined, "📡 Probe All Trackers")}
          </button>
        </div>
      </div>

      {/* BULK IMPORT MODAL */}
      <BulkImportModal isOpen={isModalOpen} onClose={handleClose} />
    </div>
  );
}

export default ImportTools;
