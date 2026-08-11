import { useState, useMemo } from "react";
import {
  useTorrents,
  useDownloadClients,
  useDownloadPlusPlusStatus,
  useDownloadPlusPlusTrackers,
  useInspectTorrentTrackers,
  useInspectHashTrackers,
  useScanDownloadPlusPlusTrackers,
  useHarvestProwlarrTrackers,
  useHarvestFeedTrackers,
  useBoostTorrent,
  useBoostHash,
  useInjectTrackerToTorrent,
  useBoostAllTorrents,
  useAddDownloadPlusPlusTracker,
  useDeleteDownloadPlusPlusTracker,
  useDownloadHistory,
} from "../api/hooks";
import { formatBytes, formatRatio } from "../utils/formatters";
import { useToast } from "../context/ToastContext";

interface UnifiedDownloadItem {
  key: string;
  id?: number;
  infoHash: string;
  name: string;
  totalSize: number;
  ratio: number;
  seeders: number;
  isPrivate: boolean;
  sourceType: "real_client" | "seedarr";
  clientName: string;
}

function DownloadPlusPlus() {
  const { data: torrents, isLoading: torrentsLoading } = useTorrents();
  const { data: downloadClients } = useDownloadClients();
  const { data: status } = useDownloadPlusPlusStatus();
  const { data: trackers, isLoading: trackersLoading } = useDownloadPlusPlusTrackers();
  const { data: history } = useDownloadHistory();
  const { showToast } = useToast();

  const [activeTab, setActiveTab] = useState<"booster" | "radar" | "settings">("booster");
  const [downloadFilter, setDownloadFilter] = useState<"all" | "real" | "seedarr">("all");
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [trackerSearch, setTrackerSearch] = useState("");
  const [sourceFilter, setSourceFilter] = useState<string>("all");
  const [healthFilter, setHealthFilter] = useState<string>("all");
  const [newTrackerUrl, setNewTrackerUrl] = useState("");
  const [isAddingTracker, setIsAddingTracker] = useState(false);

  // Build unified items list
  const unifiedItems = useMemo<UnifiedDownloadItem[]>(() => {
    const list: UnifiedDownloadItem[] = [];

    // Seedarr library torrents
    (torrents ?? []).forEach((t) => {
      list.push({
        key: `seedarr-${t.id}`,
        id: t.id,
        infoHash: t.infoHash || "",
        name: t.name,
        totalSize: t.totalSize,
        ratio: t.ratio,
        seeders: t.seeders,
        isPrivate: t.isPrivate,
        sourceType: "seedarr",
        clientName: "Seedarr Seeder",
      });
    });

    return list;
  }, [torrents]);

  const filteredDownloads = useMemo(() => {
    return unifiedItems.filter((item) => {
      if (item.isPrivate) return false;
      if (downloadFilter === "real" && item.sourceType !== "real_client") return false;
      if (downloadFilter === "seedarr" && item.sourceType !== "seedarr") return false;
      return true;
    });
  }, [unifiedItems, downloadFilter]);

  const activeSelectedKey = selectedKey ?? filteredDownloads[0]?.key ?? "";
  const selectedItem = useMemo(() => {
    return filteredDownloads.find((i) => i.key === activeSelectedKey);
  }, [filteredDownloads, activeSelectedKey]);

  // Inspection hooks
  const { data: torrentInspection, isLoading: torrentInspectLoading } = useInspectTorrentTrackers(
    selectedItem?.id ?? 0,
    Boolean(selectedItem?.id && selectedItem.id > 0),
  );

  const { data: hashInspection, isLoading: hashInspectLoading } = useInspectHashTrackers(
    selectedItem?.infoHash ?? "",
    selectedItem?.name ?? "",
    Boolean(!selectedItem?.id && selectedItem?.infoHash),
  );

  const inspection = selectedItem?.id ? torrentInspection : hashInspection;
  const inspectionLoading = selectedItem?.id ? torrentInspectLoading : hashInspectLoading;

  const scanTrackers = useScanDownloadPlusPlusTrackers();
  const harvestProwlarr = useHarvestProwlarrTrackers();
  const harvestFeeds = useHarvestFeedTrackers();
  const boostTorrent = useBoostTorrent();
  const boostHash = useBoostHash();
  const injectTracker = useInjectTrackerToTorrent();
  const boostAll = useBoostAllTorrents();
  const addTracker = useAddDownloadPlusPlusTracker();
  const deleteTracker = useDeleteDownloadPlusPlusTracker();

  const handleScanAll = () => {
    scanTrackers.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Probed ${res.testedCount} tracker endpoints`, "success");
      },
      onError: (err) => {
        showToast(`Failed to probe trackers: ${err.message}`, "error");
      },
    });
  };

  const handleHarvestProwlarr = () => {
    harvestProwlarr.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Harvested ${res.harvestedCount} trackers from Prowlarr`, "success");
      },
      onError: (err) => {
        showToast(`Failed to harvest from Prowlarr: ${err.message}`, "error");
      },
    });
  };

  const handleHarvestFeeds = () => {
    harvestFeeds.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Harvested ${res.harvestedCount} trackers from public feeds`, "success");
      },
      onError: (err) => {
        showToast(`Failed to harvest from feeds: ${err.message}`, "error");
      },
    });
  };

  const handleBoostSelectedItem = () => {
    if (!selectedItem) return;

    if (selectedItem.id && selectedItem.id > 0) {
      boostTorrent.mutate(selectedItem.id, {
        onSuccess: (res) => {
          showToast(res.message, res.boosted ? "success" : "info");
        },
        onError: (err) => {
          showToast(`Failed to boost: ${err.message}`, "error");
        },
      });
    } else if (selectedItem.infoHash) {
      boostHash.mutate(
        { infoHash: selectedItem.infoHash, name: selectedItem.name },
        {
          onSuccess: (res) => {
            showToast(res.message, res.boosted ? "success" : "info");
          },
          onError: (err) => {
            showToast(`Failed to boost: ${err.message}`, "error");
          },
        },
      );
    }
  };

  const handleInjectSingle = (trackerUrl: string) => {
    if (!selectedItem) return;

    injectTracker.mutate(
      {
        torrentId: selectedItem.id,
        infoHash: selectedItem.infoHash,
        trackerUrl,
      },
      {
        onSuccess: (res) => {
          showToast(res.message, "success");
        },
        onError: (err) => {
          showToast(`Failed to inject tracker: ${err.message}`, "error");
        },
      },
    );
  };

  const handleBoostAll = () => {
    boostAll.mutate(undefined, {
      onSuccess: (resList) => {
        const totalAdded = resList.reduce((sum, r) => sum + r.addedTrackersCount, 0);
        showToast(
          `Boosted swarms across ${resList.length} downloads (+${totalAdded} trackers injected into Seedarr & download clients)`,
          "success",
        );
      },
      onError: (err) => {
        showToast(`Failed to boost downloads: ${err.message}`, "error");
      },
    });
  };

  const handleAddCustomTracker = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTrackerUrl.trim()) return;
    addTracker.mutate(
      { url: newTrackerUrl.trim() },
      {
        onSuccess: () => {
          showToast("Custom tracker added successfully", "success");
          setNewTrackerUrl("");
          setIsAddingTracker(false);
        },
        onError: (err) => {
          showToast(`Failed to add tracker: ${err.message}`, "error");
        },
      },
    );
  };

  const filteredTrackers = useMemo(() => {
    return (trackers ?? []).filter((t) => {
      if (trackerSearch.trim()) {
        const q = trackerSearch.toLowerCase();
        if (
          !t.url.toLowerCase().includes(q) &&
          !t.host.toLowerCase().includes(q) &&
          !t.sourceName.toLowerCase().includes(q)
        ) {
          return false;
        }
      }
      if (sourceFilter !== "all") {
        if (sourceFilter === "prowlarr" && t.source !== "Prowlarr" && t.source !== 1) return false;
        if (sourceFilter === "feeds" && t.source !== "PublicList" && t.source !== 0) return false;
        if (sourceFilter === "manual" && t.source !== "Manual" && t.source !== 3) return false;
      }
      if (healthFilter !== "all") {
        if (healthFilter === "alive" && t.status !== "Alive" && t.status !== 1) return false;
        if (healthFilter === "slow" && t.status !== "Slow" && t.status !== 2) return false;
        if (healthFilter === "offline" && t.status !== "Offline" && t.status !== 3) return false;
        if (healthFilter === "untested" && t.status !== "Untested" && t.status !== 0) return false;
      }
      return true;
    });
  }, [trackers, trackerSearch, sourceFilter, healthFilter]);

  const enabledClientsCount = useMemo(() => {
    return (downloadClients ?? []).filter((c) => c.enable).length;
  }, [downloadClients]);

  return (
    <div>
      {/* Top Header Row */}
      <div
        className="page-heading-row"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1rem",
        }}
      >
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
            <h1 className="page-heading" style={{ margin: 0 }}>
              ⚡ Download++
            </h1>
            <span className="badge badge-primary">Real & Simulated Swarm Booster</span>
          </div>
          <div style={{ fontSize: "0.85rem", color: "var(--text-muted)", marginTop: "0.25rem" }}>
            Detects trackers on live swarms and accelerates real downloads in qBittorrent, Transmission & Deluge
          </div>
        </div>

        {/* Tab switcher */}
        <div className="tab-nav" style={{ margin: 0 }}>
          <button
            className={`tab-btn ${activeTab === "booster" ? "tab-btn-active" : ""}`}
            onClick={() => setActiveTab("booster")}
          >
            ⚡ Swarm Booster
          </button>
          <button
            className={`tab-btn ${activeTab === "radar" ? "tab-btn-active" : ""}`}
            onClick={() => setActiveTab("radar")}
          >
            📡 Tracker Radar ({trackers?.length || 0})
          </button>
          <button
            className={`tab-btn ${activeTab === "settings" ? "tab-btn-active" : ""}`}
            onClick={() => setActiveTab("settings")}
          >
            ⚙️ Sources & Automation
          </button>
        </div>
      </div>

      {/* Global Metric Cards */}
      <div className="stats-grid" style={{ marginBottom: "1.25rem" }}>
        <div className="stat-card">
          <div className="stat-value">{status?.totalTrackersMonitored ?? 0}</div>
          <div className="stat-label">Trackers Monitored</div>
        </div>
        <div className="stat-card">
          <div className="stat-value" style={{ color: "var(--success)" }}>
            {status?.aliveTrackersCount ?? 0}
          </div>
          <div className="stat-label">Alive & Responsive</div>
        </div>
        <div className="stat-card">
          <div className="stat-value" style={{ color: "var(--accent)" }}>
            {status?.prowlarrTrackersCount ?? 0}
          </div>
          <div className="stat-label">From Prowlarr</div>
        </div>
        <div className="stat-card">
          <div className="stat-value">{status?.torrentsBoostedCount ?? 0}</div>
          <div className="stat-label">Swarms Boosted</div>
        </div>
      </div>

      {/* TAB 1: SWARM BOOSTER & TORRENT DETECTOR */}
      {activeTab === "booster" && (
        <div>
          {/* Action Toolbar */}
          <div
            className="card"
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "1rem",
              marginBottom: "1rem",
              padding: "0.75rem 1rem",
            }}
          >
            <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
              <button
                className="btn btn-primary"
                onClick={handleBoostAll}
                disabled={boostAll.isPending || filteredDownloads.length === 0}
                title="Inject optimal verified alive trackers into real download agents and Seedarr"
              >
                {boostAll.isPending ? "Boosting All Swarms..." : "⚡ Boost All Downloads"}
              </button>

              <button
                className="btn btn-outline"
                onClick={handleHarvestProwlarr}
                disabled={harvestProwlarr.isPending}
                title="Harvest public indexer tracker URLs from connected Prowlarr instance"
              >
                {harvestProwlarr.isPending ? "Harvesting..." : "🔄 Harvest from Prowlarr"}
              </button>

              <button
                className="btn btn-outline"
                onClick={handleScanAll}
                disabled={scanTrackers.isPending}
                title="Ping and probe health across all monitored tracker endpoints"
              >
                {scanTrackers.isPending ? "Probing..." : "📡 Probe All Trackers"}
              </button>
            </div>

            <div style={{ display: "flex", gap: "0.75rem", alignItems: "center", fontSize: "0.8rem", color: "var(--text-muted)" }}>
              <span>🔗 {enabledClientsCount} Download Agent(s) Connected</span>
              <span>•</span>
              <span>{filteredDownloads.length} Public Downloads Eligible</span>
            </div>
          </div>

          {/* Master-Detail Split Layout */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "360px 1fr",
              gap: "1rem",
              alignItems: "start",
            }}
          >
            {/* Left Master List: Torrents */}
            <div className="card" style={{ padding: "0.75rem", maxHeight: "calc(100vh - 280px)", overflow: "auto" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", margin: "0.25rem 0.5rem 0.75rem" }}>
                <h3 style={{ margin: 0, fontSize: "1rem" }}>Select Download</h3>
                <div style={{ display: "flex", gap: "0.25rem" }}>
                  <button
                    className={`btn btn-small ${downloadFilter === "all" ? "btn-primary" : "btn-outline"}`}
                    style={{ fontSize: "0.7rem", padding: "0.15rem 0.4rem" }}
                    onClick={() => setDownloadFilter("all")}
                  >
                    All
                  </button>
                  <button
                    className={`btn btn-small ${downloadFilter === "real" ? "btn-primary" : "btn-outline"}`}
                    style={{ fontSize: "0.7rem", padding: "0.15rem 0.4rem" }}
                    onClick={() => setDownloadFilter("real")}
                  >
                    Clients
                  </button>
                  <button
                    className={`btn btn-small ${downloadFilter === "seedarr" ? "btn-primary" : "btn-outline"}`}
                    style={{ fontSize: "0.7rem", padding: "0.15rem 0.4rem" }}
                    onClick={() => setDownloadFilter("seedarr")}
                  >
                    Seedarr
                  </button>
                </div>
              </div>

              {torrentsLoading && <p className="loading">Loading downloads...</p>}
              {!torrentsLoading && filteredDownloads.length === 0 && (
                <p className="loading">No active non-private downloads available.</p>
              )}

              <div style={{ display: "flex", flexDirection: "column", gap: "0.5rem" }}>
                {filteredDownloads.map((item) => {
                  const isSelected = item.key === activeSelectedKey;
                  const match = history?.find(
                    (h) =>
                      (item.infoHash && h.infoHash?.toLowerCase() === item.infoHash.toLowerCase()) ||
                      h.title === item.name,
                  );
                  const meta = match?.metadata;

                  return (
                    <div
                      key={item.key}
                      onClick={() => setSelectedKey(item.key)}
                      style={{
                        padding: "0.6rem 0.75rem",
                        borderRadius: "6px",
                        border: isSelected ? "2px solid var(--accent)" : "1px solid var(--border-light)",
                        backgroundColor: isSelected ? "var(--bg-secondary)" : "var(--bg-primary)",
                        cursor: "pointer",
                        display: "flex",
                        gap: "0.6rem",
                        alignItems: "center",
                      }}
                    >
                      {meta?.posterUrl ? (
                        <img
                          src={meta.posterUrl}
                          alt=""
                          style={{
                            width: "26px",
                            height: "38px",
                            objectFit: "cover",
                            borderRadius: "3px",
                            flexShrink: 0,
                          }}
                        />
                      ) : (
                        <span style={{ fontSize: "1.2rem", flexShrink: 0 }}>
                          {item.sourceType === "real_client" ? "⚡" : "📦"}
                        </span>
                      )}

                      <div style={{ minWidth: 0, flex: 1 }}>
                        <div
                          style={{
                            fontWeight: 600,
                            fontSize: "0.85rem",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {meta?.title || item.name}
                        </div>
                        <div
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            fontSize: "0.75rem",
                            color: "var(--text-muted)",
                            marginTop: "0.2rem",
                          }}
                        >
                          <span>{formatBytes(item.totalSize)}</span>
                          <span>Ratio: {formatRatio(item.ratio)}</span>
                          <span style={{ color: item.seeders <= 2 ? "var(--warning)" : "inherit" }}>
                            {item.seeders} Seeders
                          </span>
                        </div>
                        <div style={{ marginTop: "0.25rem" }}>
                          <span
                            className={`badge ${
                              item.sourceType === "real_client" ? "badge-primary" : "badge-secondary"
                            }`}
                            style={{ fontSize: "0.65rem", padding: "0.1rem 0.35rem" }}
                          >
                            {item.clientName}
                          </span>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Right Detail: Per-Torrent Tracker Detection Matrix */}
            <div className="card" style={{ padding: "1rem" }}>
              {selectedItem ? (
                <div>
                  {/* Torrent Header Info & Actions */}
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      flexWrap: "wrap",
                      gap: "1rem",
                      paddingBottom: "1rem",
                      borderBottom: "1px solid var(--border-light)",
                      marginBottom: "1rem",
                    }}
                  >
                    <div>
                      <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                        <h2 style={{ margin: 0, fontSize: "1.2rem" }}>{selectedItem.name}</h2>
                        <span className="badge badge-primary" style={{ fontSize: "0.7rem" }}>
                          {selectedItem.clientName}
                        </span>
                      </div>
                      <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", marginTop: "0.3rem" }}>
                        <code>{selectedItem.infoHash}</code> • {formatBytes(selectedItem.totalSize)}
                      </div>
                    </div>

                    <button
                      className="btn btn-primary"
                      onClick={handleBoostSelectedItem}
                      disabled={boostTorrent.isPending || boostHash.isPending}
                      title="Auto-inject top verified alive trackers into this swarm across Seedarr & download clients"
                    >
                      {boostTorrent.isPending || boostHash.isPending ? "Injecting..." : "⚡ Boost This Swarm"}
                    </button>
                  </div>

                  {/* Inspection Summary Matrix */}
                  {inspectionLoading ? (
                    <p className="loading">Inspecting torrent presence across all known trackers...</p>
                  ) : inspection ? (
                    <div>
                      <div
                        style={{
                          display: "flex",
                          gap: "1rem",
                          flexWrap: "wrap",
                          marginBottom: "1rem",
                        }}
                      >
                        <div
                          style={{
                            padding: "0.5rem 0.8rem",
                            borderRadius: "4px",
                            backgroundColor: "var(--bg-primary)",
                            border: "1px solid var(--border-light)",
                          }}
                        >
                          <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>Checked Trackers: </span>
                          <strong>{inspection.totalTrackersChecked}</strong>
                        </div>
                        <div
                          style={{
                            padding: "0.5rem 0.8rem",
                            borderRadius: "4px",
                            backgroundColor: "var(--bg-primary)",
                            border: "1px solid var(--border-light)",
                          }}
                        >
                          <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>Currently Attached: </span>
                          <strong style={{ color: "var(--accent)" }}>{inspection.attachedTrackersCount}</strong>
                        </div>
                        <div
                          style={{
                            padding: "0.5rem 0.8rem",
                            borderRadius: "4px",
                            backgroundColor: "var(--bg-primary)",
                            border: "1px solid var(--border-light)",
                          }}
                        >
                          <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>Active Swarms Detected: </span>
                          <strong style={{ color: "var(--success)" }}>{inspection.detectedTrackersCount}</strong>
                        </div>
                      </div>

                      {/* Search / Filter Table for this torrent */}
                      <div className="torrent-table-wrapper" style={{ maxHeight: "calc(100vh - 440px)", overflow: "auto" }}>
                        <table className="torrent-table">
                          <thead>
                            <tr>
                              <th className="torrent-table-th">Tracker Host</th>
                              <th className="torrent-table-th">Source</th>
                              <th className="torrent-table-th">Protocol</th>
                              <th className="torrent-table-th">Detection State</th>
                              <th className="torrent-table-th">Seeders</th>
                              <th className="torrent-table-th">Leechers</th>
                              <th className="torrent-table-th">Latency</th>
                              <th className="torrent-table-th" style={{ textAlign: "right" }}>Action</th>
                            </tr>
                          </thead>
                          <tbody>
                            {inspection.detections.map((det) => (
                              <tr key={det.trackerId} className="torrent-table-row">
                                <td>
                                  <div>
                                    <strong>{det.trackerHost}</strong>
                                    <div style={{ fontSize: "0.7rem", color: "var(--text-muted)" }}>
                                      {det.trackerUrl}
                                    </div>
                                  </div>
                                </td>
                                <td>
                                  <span
                                    className={`badge ${
                                      det.source === "Prowlarr" || det.source === 1
                                        ? "badge-primary"
                                        : "badge-secondary"
                                    }`}
                                    style={{ fontSize: "0.7rem" }}
                                  >
                                    {det.sourceName}
                                  </span>
                                </td>
                                <td>
                                  <span className="badge badge-secondary" style={{ fontSize: "0.7rem" }}>
                                    {det.protocol}
                                  </span>
                                </td>
                                <td>
                                  <span
                                    className={`badge ${
                                      det.isAttached
                                        ? "badge-seeding"
                                        : det.isDetected
                                        ? "badge-success"
                                        : "badge-stopped"
                                    }`}
                                    style={{ fontSize: "0.7rem" }}
                                  >
                                    {det.detectionStatus}
                                  </span>
                                </td>
                                <td>{det.seeders > 0 ? det.seeders : "-"}</td>
                                <td>{det.leechers > 0 ? det.leechers : "-"}</td>
                                <td>{det.latencyMs > 0 ? `${det.latencyMs} ms` : "-"}</td>
                                <td style={{ textAlign: "right" }}>
                                  {det.isAttached ? (
                                    <span style={{ fontSize: "0.75rem", color: "var(--success)" }}>
                                      ✓ In Swarm
                                    </span>
                                  ) : (
                                    <button
                                      className="btn btn-small btn-outline"
                                      style={{ fontSize: "0.75rem", padding: "0.2rem 0.5rem" }}
                                      onClick={() => handleInjectSingle(det.trackerUrl)}
                                      disabled={injectTracker.isPending}
                                      title="Inject this tracker into Seedarr and connected download client"
                                    >
                                      ⚡ Inject
                                    </button>
                                  )}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  ) : null}
                </div>
              ) : (
                <p className="loading">Select a download on the left to inspect its tracker detection matrix.</p>
              )}
            </div>
          </div>
        </div>
      )}

      {/* TAB 2: TRACKER RADAR (LIVE HEALTH MATRIX) */}
      {activeTab === "radar" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          {/* Filter & Search Bar */}
          <div
            className="card"
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "1rem",
              padding: "0.75rem 1rem",
            }}
          >
            <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap", flex: 1 }}>
              <input
                type="text"
                className="input"
                placeholder="Search tracker host, URL, or provider..."
                value={trackerSearch}
                onChange={(e) => setTrackerSearch(e.target.value)}
                style={{ minWidth: "220px", padding: "0.35rem 0.6rem", fontSize: "0.85rem" }}
              />

              <select
                value={sourceFilter}
                onChange={(e) => setSourceFilter(e.target.value)}
                style={{
                  padding: "0.35rem 0.6rem",
                  borderRadius: "4px",
                  backgroundColor: "var(--bg-secondary)",
                  color: "inherit",
                  border: "1px solid var(--border)",
                }}
              >
                <option value="all">All Sources</option>
                <option value="prowlarr">Prowlarr Indexers</option>
                <option value="feeds">Curated Public Feeds</option>
                <option value="manual">Manual Trackers</option>
              </select>

              <select
                value={healthFilter}
                onChange={(e) => setHealthFilter(e.target.value)}
                style={{
                  padding: "0.35rem 0.6rem",
                  borderRadius: "4px",
                  backgroundColor: "var(--bg-secondary)",
                  color: "inherit",
                  border: "1px solid var(--border)",
                }}
              >
                <option value="all">All Health States</option>
                <option value="alive">🟢 Alive</option>
                <option value="slow">🟡 Slow</option>
                <option value="offline">🔴 Offline</option>
                <option value="untested">⚪ Untested</option>
              </select>
            </div>

            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button
                className="btn btn-outline"
                onClick={() => setIsAddingTracker(!isAddingTracker)}
              >
                {isAddingTracker ? "Cancel" : "+ Add Custom Tracker"}
              </button>

              <button
                className="btn btn-primary"
                onClick={handleScanAll}
                disabled={scanTrackers.isPending}
              >
                {scanTrackers.isPending ? "Probing..." : "📡 Probe All Trackers"}
              </button>
            </div>
          </div>

          {/* Add Tracker Form */}
          {isAddingTracker && (
            <form
              onSubmit={handleAddCustomTracker}
              className="card"
              style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}
            >
              <input
                type="text"
                className="input"
                placeholder="udp://tracker.example.com:1337/announce or http://..."
                value={newTrackerUrl}
                onChange={(e) => setNewTrackerUrl(e.target.value)}
                style={{ flex: 1, padding: "0.4rem 0.75rem" }}
              />
              <button type="submit" className="btn btn-success" disabled={addTracker.isPending}>
                Save Tracker
              </button>
            </form>
          )}

          {/* Trackers Table */}
          <div className="card" style={{ padding: 0, overflow: "hidden" }}>
            {trackersLoading ? (
              <p className="loading" style={{ padding: "1rem" }}>
                Loading trackers...
              </p>
            ) : (
              <div className="torrent-table-wrapper" style={{ maxHeight: "calc(100vh - 340px)", overflow: "auto" }}>
                <table className="torrent-table">
                  <thead>
                    <tr>
                      <th className="torrent-table-th">Tracker Endpoint</th>
                      <th className="torrent-table-th">Source</th>
                      <th className="torrent-table-th">Protocol</th>
                      <th className="torrent-table-th">Health Status</th>
                      <th className="torrent-table-th">Latency</th>
                      <th className="torrent-table-th">Successful Probes</th>
                      <th className="torrent-table-th">Failed Probes</th>
                      <th className="torrent-table-th">Swarms Found</th>
                      <th className="torrent-table-th" style={{ textAlign: "right" }}>
                        Actions
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredTrackers.length === 0 ? (
                      <tr>
                        <td colSpan={9} className="torrent-table-empty">
                          No trackers match your filter.
                        </td>
                      </tr>
                    ) : (
                      filteredTrackers.map((t) => (
                        <tr key={t.id} className="torrent-table-row">
                          <td>
                            <div>
                              <strong>{t.host}</strong>
                              <div style={{ fontSize: "0.7rem", color: "var(--text-muted)" }}>
                                {t.url}
                              </div>
                            </div>
                          </td>
                          <td>
                            <span className="badge badge-secondary" style={{ fontSize: "0.7rem" }}>
                              {t.sourceName}
                            </span>
                          </td>
                          <td>
                            <span className="badge badge-primary" style={{ fontSize: "0.7rem" }}>
                              {t.protocol}
                            </span>
                          </td>
                          <td>
                            <span
                              className={`badge ${
                                t.status === "Alive" || t.status === 1
                                  ? "badge-success"
                                  : t.status === "Slow" || t.status === 2
                                  ? "badge-warning"
                                  : t.status === "Offline" || t.status === 3
                                  ? "badge-danger"
                                  : "badge-secondary"
                              }`}
                              style={{ fontSize: "0.7rem" }}
                            >
                              {t.status === 1
                                ? "Alive"
                                : t.status === 2
                                ? "Slow"
                                : t.status === 3
                                ? "Offline"
                                : t.status === 0
                                ? "Untested"
                                : t.status}
                            </span>
                          </td>
                          <td>{t.latencyMs > 0 ? `${t.latencyMs} ms` : "-"}</td>
                          <td>{t.successfulScrapes}</td>
                          <td>{t.failedScrapes}</td>
                          <td>{t.totalSwarmsFound}</td>
                          <td style={{ textAlign: "right" }}>
                            <button
                              className="btn btn-small btn-danger"
                              style={{ padding: "0.15rem 0.4rem", fontSize: "0.75rem" }}
                              onClick={() => deleteTracker.mutate(t.id)}
                              disabled={deleteTracker.isPending}
                              title="Delete tracker"
                            >
                              ✕
                            </button>
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}

      {/* TAB 3: SOURCES & AUTOMATION SETTINGS */}
      {activeTab === "settings" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>Connected Download Agents</h3>
            <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 1rem 0" }}>
              Download++ dynamically coordinates with your configured download clients (qBittorrent, Transmission, Deluge)
              to inject alive trackers into your active physical downloads.
            </p>
            <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
              {(downloadClients ?? [])
                .filter((c) => c.enable)
                .map((client) => (
                  <span key={client.id} className="badge badge-primary" style={{ padding: "0.4rem 0.75rem", fontSize: "0.85rem" }}>
                    ⚡ {client.name} ({client.clientType})
                  </span>
                ))}
              {enabledClientsCount === 0 && (
                <span style={{ fontSize: "0.85rem", color: "var(--warning)" }}>
                  No download agents currently configured. Add qBittorrent or Transmission in Settings ⚙️ to boost real downloads.
                </span>
              )}
            </div>
          </div>

          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>Prowlarr Tracker Harvesting</h3>
            <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 1rem 0" }}>
              Download++ queries your configured Prowlarr connection to extract public and semi-public indexer announce URLs.
              Private indexers with sensitive passkeys are automatically filtered out.
            </p>
            <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
              <button
                className="btn btn-primary"
                onClick={handleHarvestProwlarr}
                disabled={harvestProwlarr.isPending}
              >
                {harvestProwlarr.isPending ? "Harvesting..." : "🔄 Sync Trackers from Prowlarr"}
              </button>
              {status?.lastProwlarrHarvestTime && (
                <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                  Last Harvest: {new Date(status.lastProwlarrHarvestTime).toLocaleString()}
                </span>
              )}
            </div>
          </div>

          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>Curated Public Tracker Feeds</h3>
            <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 1rem 0" }}>
              Pulls live, high-uptime public tracker endpoints from GitHub curated lists (ngosang trackers_best, XIU2).
            </p>
            <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
              <button
                className="btn btn-outline"
                onClick={handleHarvestFeeds}
                disabled={harvestFeeds.isPending}
              >
                {harvestFeeds.isPending ? "Downloading Feeds..." : "🌐 Sync Curated Feeds"}
              </button>
              {status?.lastScanTime && (
                <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                  Last Probed: {new Date(status.lastScanTime).toLocaleString()}
                </span>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default DownloadPlusPlus;
