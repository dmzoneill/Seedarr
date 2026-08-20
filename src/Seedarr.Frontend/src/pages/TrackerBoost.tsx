import { useState, useMemo } from "react";
import {
  useTorrents,
  useDownloadHistory,
  useDownloadClients,
  useTrackerBoostStatus,
  useTrackerBoostTrackers,
  useTrackerBoostSettings,
  useUpdateTrackerBoostSettings,
  useTrackerBoostMatrix,
  useInspectTorrentTrackers,
  useInspectHashTrackers,
  useScanTrackerBoostTrackers,
  useHarvestDownloadTrackers,
  useHarvestProwlarrTrackers,
  useHarvestFeedTrackers,
  useBoostTorrent,
  useBoostHash,
  useInjectTrackerToTorrent,
  useBoostAllTorrents,
  useAddTrackerBoostTracker,
  useDeleteTrackerBoostTracker,
} from "../api/hooks";
import { formatBytes, formatRatio } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import type { TrackerBoostSettings } from "../api/types";

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

function TrackerBoost() {
  const { data: torrents, isLoading: torrentsLoading } = useTorrents();
  const { data: history } = useDownloadHistory();
  const { data: downloadClients } = useDownloadClients();
  const { data: status } = useTrackerBoostStatus();
  const { data: trackers, isLoading: trackersLoading } =
    useTrackerBoostTrackers();
  const { data: settings } = useTrackerBoostSettings();
  const updateSettings = useUpdateTrackerBoostSettings();
  const { data: matrixData, isLoading: matrixLoading } = useTrackerBoostMatrix();
  const { showToast } = useToast();

  const [activeTab, setActiveTab] = useState<"booster" | "matrix" | "radar" | "settings">(
    "booster",
  );
  const [downloadFilter, setDownloadFilter] = useState<
    "all" | "public" | "private" | "real" | "seedarr"
  >("all");
  const [downloadSearch, setDownloadSearch] = useState("");
  const [matrixViewMode, setMatrixViewMode] = useState<"by_torrent" | "by_tracker">("by_torrent");
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [trackerSearch, setTrackerSearch] = useState("");
  const [sourceFilter, setSourceFilter] = useState<string>("all");
  const [healthFilter, setHealthFilter] = useState<string>("all");
  const [newTrackerUrl, setNewTrackerUrl] = useState("");
  const [isAddingTracker, setIsAddingTracker] = useState(false);

  // Build unified items list
  const unifiedItems = useMemo<UnifiedDownloadItem[]>(() => {
    const list: UnifiedDownloadItem[] = [];
    const seenHashes = new Set<string>();

    (torrents ?? []).forEach((t) => {
      const hash = (t.infoHash || "").toLowerCase();
      if (hash) seenHashes.add(hash);
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

    (history ?? []).forEach((h) => {
      const hash = (h.infoHash || "").toLowerCase();
      if (hash && !seenHashes.has(hash)) {
        seenHashes.add(hash);
        list.push({
          key: `history-${h.id}`,
          id: h.torrentId || 0,
          infoHash: h.infoHash,
          name: h.title,
          totalSize: h.totalSize,
          ratio: 0,
          seeders: 0,
          isPrivate: h.isPrivate,
          sourceType: "real_client",
          clientName: h.clientName || "Download Client",
        });
      }
    });

    return list;
  }, [torrents, history]);

  const filteredDownloads = useMemo(() => {
    return unifiedItems.filter((item) => {
      if (downloadFilter === "public" && item.isPrivate) return false;
      if (downloadFilter === "private" && !item.isPrivate) return false;
      if (downloadFilter === "real" && item.sourceType !== "real_client") return false;
      if (downloadFilter === "seedarr" && item.sourceType !== "seedarr") return false;
      if (downloadSearch.trim()) {
        const q = downloadSearch.toLowerCase();
        if (!item.name.toLowerCase().includes(q) && !item.infoHash.toLowerCase().includes(q)) {
          return false;
        }
      }
      return true;
    });
  }, [unifiedItems, downloadFilter, downloadSearch]);

  const activeSelectedKey = selectedKey ?? filteredDownloads[0]?.key ?? "";
  const selectedItem = useMemo(() => {
    return filteredDownloads.find((i) => i.key === activeSelectedKey);
  }, [filteredDownloads, activeSelectedKey]);

  // Inspection hooks with live hash scraping
  const { data: torrentInspection, isLoading: torrentInspectLoading, refetch: refetchTorrentInspect } =
    useInspectTorrentTrackers(
      selectedItem?.id ?? 0,
      Boolean(selectedItem?.id && selectedItem.id > 0),
    );

  const { data: hashInspection, isLoading: hashInspectLoading, refetch: refetchHashInspect } =
    useInspectHashTrackers(
      selectedItem?.infoHash ?? "",
      selectedItem?.name ?? "",
      Boolean(!selectedItem?.id && selectedItem?.infoHash),
    );

  const inspection = selectedItem?.id ? torrentInspection : hashInspection;
  const inspectionLoading = selectedItem?.id
    ? torrentInspectLoading
    : hashInspectLoading;

  const scanTrackers = useScanTrackerBoostTrackers();
  const harvestDownloads = useHarvestDownloadTrackers();
  const harvestProwlarr = useHarvestProwlarrTrackers();
  const harvestFeeds = useHarvestFeedTrackers();
  const boostTorrent = useBoostTorrent();
  const boostHash = useBoostHash();
  const injectTracker = useInjectTrackerToTorrent();
  const boostAll = useBoostAllTorrents();
  const addTracker = useAddTrackerBoostTracker();
  const deleteTracker = useDeleteTrackerBoostTracker();

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

  const handleHarvestDownloads = () => {
    harvestDownloads.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`Harvested ${res.harvestedCount} new trackers from active downloads`, "success");
      },
      onError: (err) => {
        showToast(`Failed to harvest from downloads: ${err.message}`, "error");
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

  const handleBoostItem = (item: UnifiedDownloadItem) => {
    if (item.id && item.id > 0) {
      boostTorrent.mutate(item.id, {
        onSuccess: (res) => {
          showToast(res.message, res.boosted ? "success" : "info");
        },
        onError: (err) => {
          showToast(`Failed to boost: ${err.message}`, "error");
        },
      });
    } else if (item.infoHash) {
      boostHash.mutate(
        { infoHash: item.infoHash, name: item.name },
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
        const totalAdded = resList.reduce(
          (sum, r) => sum + r.addedTrackersCount,
          0,
        );
        const totalSeeds = resList.reduce(
          (sum, r) => sum + r.totalSeedersFound,
          0,
        );
        showToast(
          `Boosted ${resList.length} swarms: injected ${totalAdded} verified trackers (+${totalSeeds} seeds discovered)`,
          "success",
        );
      },
      onError: (err) => {
        showToast(`Failed to boost downloads: ${err.message}`, "error");
      },
    });
  };

  const handleToggleSetting = (key: keyof TrackerBoostSettings) => {
    if (!settings) return;
    const updated = { ...settings, [key]: !settings[key] };
    updateSettings.mutate(updated, {
      onSuccess: () => {
        showToast("TrackerBoost settings updated", "success");
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
        if (sourceFilter === "active" && t.source !== "ActiveTorrent" && t.source !== 4) return false;
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
    <div className="content-area">
      {/* Top Header Row */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1.25rem",
        }}
      >
        <div className="page-header-group">
          <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
            <h1 className="page-heading" style={{ margin: 0, padding: 0, background: "transparent", border: "none" }}>
              Tracker Boost
            </h1>
            <span className="badge badge-primary">⚡ Smart Booster</span>
            <span className="badge badge-secondary">BEP 15 & 48 Scraper</span>
          </div>
          <div
            style={{
              fontSize: "0.85rem",
              color: "var(--text-muted)",
              marginTop: "0.3rem",
            }}
          >
            Scrapes live tracker swarms by info_hash to discover and inject verified seeders/peers into Seedarr and download clients
          </div>
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
            {status?.activeTorrentTrackersCount ?? 0}
          </div>
          <div className="stat-label">Harvested from Swarms</div>
        </div>
        <div className="stat-card">
          <div className="stat-value" style={{ color: "#38bdf8" }}>
            {status?.torrentsBoostedCount ?? 0}
          </div>
          <div className="stat-label">Swarms Boosted</div>
        </div>
      </div>

      {/* Tab Navigation Bar placed right above content */}
      <div
        style={{
          display: "flex",
          gap: "0.5rem",
          alignItems: "center",
          marginBottom: "1.25rem",
          paddingBottom: "0.85rem",
          borderBottom: "1px solid var(--border-light)",
          flexWrap: "wrap",
        }}
      >
        <button
          className={`btn ${activeTab === "booster" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("booster")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "booster" ? 600 : 500,
          }}
        >
          ⚡ Swarm Optimizer
        </button>
        <button
          className={`btn ${activeTab === "matrix" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("matrix")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "matrix" ? 600 : 500,
          }}
        >
          📊 Cross-Matrix
        </button>
        <button
          className={`btn ${activeTab === "radar" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("radar")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "radar" ? 600 : 500,
          }}
        >
          📡 Tracker Radar ({trackers?.length || 0})
        </button>
        <button
          className={`btn ${activeTab === "settings" ? "btn-primary" : ""}`}
          onClick={() => setActiveTab("settings")}
          style={{
            padding: "0.5rem 1.15rem",
            fontSize: "0.88rem",
            fontWeight: activeTab === "settings" ? 600 : 500,
          }}
        >
          ⚙️ Sources & Automation
        </button>
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
              marginBottom: "1.25rem",
              padding: "0.75rem 1rem",
              borderRadius: "8px",
            }}
          >
            <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
              <button
                className="btn btn-primary"
                onClick={handleBoostAll}
                disabled={boostAll.isPending || filteredDownloads.length === 0}
                title="Scrape candidate trackers and inject only verified positive matches across all active downloads"
              >
                {boostAll.isPending ? "Scraping & Boosting..." : "⚡ Boost All Downloads (Verified Only)"}
              </button>

              <button
                className="btn btn-outline"
                onClick={handleHarvestDownloads}
                disabled={harvestDownloads.isPending}
                title="Extract and discover tracker URLs from active download swarms in Seedarr and download clients"
              >
                {harvestDownloads.isPending ? "Harvesting..." : "🔄 Harvest from Live Swarms"}
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

            <div style={{ display: "flex", alignItems: "center", gap: "0.5rem", flexWrap: "wrap" }}>
              <select
                className="form-control"
                style={{ width: "150px", padding: "0.35rem 0.6rem", fontSize: "0.82rem" }}
                value={downloadFilter}
                onChange={(e) => setDownloadFilter(e.target.value as any)}
              >
                <option value="all">All Swarms ({unifiedItems.length})</option>
                <option value="public">Public ({unifiedItems.filter(i => !i.isPrivate).length})</option>
                <option value="private">Private ({unifiedItems.filter(i => i.isPrivate).length})</option>
              </select>
              <input
                type="text"
                className="form-control"
                style={{ width: "200px", padding: "0.35rem 0.6rem", fontSize: "0.82rem" }}
                placeholder="Search downloads..."
                value={downloadSearch}
                onChange={(e) => setDownloadSearch(e.target.value)}
              />
            </div>
          </div>

          {/* Master-Detail Split: Left = Downloads List, Right = Live Tracker Scraper */}
          <div style={{ display: "grid", gridTemplateColumns: "360px 1fr", gap: "1.25rem", alignItems: "start" }}>
            {/* Left: Downloads List */}
            <div className="card" style={{ padding: "0.75rem", minHeight: "500px" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.75rem", paddingBottom: "0.5rem", borderBottom: "1px solid var(--border-color)" }}>
                <span style={{ fontWeight: 600, fontSize: "0.9rem" }}>Swarms ({filteredDownloads.length})</span>
                <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>Select to inspect swarm</span>
              </div>

              {torrentsLoading ? (
                <div style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>Loading downloads...</div>
              ) : filteredDownloads.length === 0 ? (
                <div style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
                  No downloads found matching filter.
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: "0.5rem", maxHeight: "650px", overflowY: "auto" }}>
                  {filteredDownloads.map((item) => {
                    const isSelected = item.key === activeSelectedKey;
                    return (
                      <div
                        key={item.key}
                        onClick={() => setSelectedKey(item.key)}
                        style={{
                          padding: "0.75rem",
                          borderRadius: "6px",
                          cursor: "pointer",
                          backgroundColor: isSelected ? "var(--accent-glow, rgba(56, 189, 248, 0.12))" : "var(--bg-secondary, rgba(255,255,255,0.02))",
                          border: isSelected ? "1px solid var(--accent, #38bdf8)" : "1px solid var(--border-color)",
                          transition: "all 0.15s ease",
                        }}
                      >
                        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: "0.5rem", marginBottom: "0.35rem" }}>
                          <span style={{ fontWeight: 600, fontSize: "0.85rem", wordBreak: "break-word" }}>
                            {item.name}
                          </span>
                          {item.isPrivate ? (
                            <span className="badge badge-secondary" style={{ fontSize: "0.7rem", whiteSpace: "nowrap" }} title="Private tracker swarm">
                              🔒 Private
                            </span>
                          ) : (
                            <span className="badge badge-success" style={{ fontSize: "0.7rem", whiteSpace: "nowrap" }} title="Public swarm boost eligible">
                              🌐 Public
                            </span>
                          )}
                        </div>
                        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", fontSize: "0.75rem", color: "var(--text-muted)" }}>
                          <span>{formatBytes(item.totalSize)} • Ratio: {formatRatio(item.ratio)}</span>
                          <span style={{ color: item.seeders > 0 ? "var(--success)" : "inherit" }}>
                            {item.seeders} Seeds
                          </span>
                        </div>
                        <div style={{ marginTop: "0.5rem", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                          <span className="badge badge-secondary" style={{ fontSize: "0.7rem" }}>
                            {item.clientName}
                          </span>
                          {!item.isPrivate ? (
                            <button
                              className="btn btn-sm btn-primary"
                              style={{ padding: "0.2rem 0.5rem", fontSize: "0.75rem" }}
                              onClick={(e) => {
                                e.stopPropagation();
                                handleBoostItem(item);
                              }}
                              title="Scrape and inject verified trackers"
                            >
                              ⚡ Enrich
                            </button>
                          ) : (
                            <span style={{ fontSize: "0.75rem", color: "var(--text-dim)" }}>Protected</span>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            {/* Right: Live Scrape Inspector Pane */}
            <div className="card" style={{ padding: "1.25rem", minHeight: "500px" }}>
              {selectedItem ? (
                <div>
                  {/* Selected Item Banner */}
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "1rem", marginBottom: "1.25rem", paddingBottom: "1rem", borderBottom: "1px solid var(--border-color)" }}>
                    <div>
                      <div style={{ display: "flex", alignItems: "center", gap: "0.5rem", marginBottom: "0.25rem", flexWrap: "wrap" }}>
                        <h2 style={{ fontSize: "1.1rem", margin: 0 }}>{selectedItem.name}</h2>
                        {selectedItem.isPrivate ? (
                          <span className="badge badge-secondary" style={{ fontSize: "0.75rem" }}>🔒 Private Swarm</span>
                        ) : (
                          <span className="badge badge-success" style={{ fontSize: "0.75rem" }}>🌐 Public Swarm</span>
                        )}
                      </div>
                      <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", fontFamily: "monospace" }}>
                        InfoHash: {selectedItem.infoHash}
                      </div>
                    </div>
                    <div style={{ display: "flex", gap: "0.5rem" }}>
                      <button
                        className="btn btn-outline"
                        style={{ fontSize: "0.85rem" }}
                        onClick={() => (selectedItem.id ? refetchTorrentInspect() : refetchHashInspect())}
                        title="Re-scrape candidate trackers for this info_hash"
                      >
                        🔄 Re-Scrape Swarm
                      </button>
                      {!selectedItem.isPrivate && (
                        <button
                          className="btn btn-primary"
                          style={{ fontSize: "0.85rem" }}
                          onClick={() => handleBoostItem(selectedItem)}
                          title="Inject verified candidate trackers into this torrent"
                        >
                          ⚡ Boost Torrent (Inject Verified)
                        </button>
                      )}
                    </div>
                  </div>

                  {selectedItem.isPrivate && (
                    <div
                      style={{
                        padding: "0.75rem 1rem",
                        marginBottom: "1.25rem",
                        borderRadius: "6px",
                        backgroundColor: "rgba(230, 126, 34, 0.12)",
                        border: "1px solid rgba(230, 126, 34, 0.35)",
                        color: "var(--text-primary)",
                        fontSize: "0.85rem",
                        display: "flex",
                        alignItems: "center",
                        gap: "0.75rem",
                      }}
                    >
                      <span style={{ fontSize: "1.25rem" }}>🔒</span>
                      <div>
                        <strong>Private Tracker Swarm:</strong> Cross-swarm public tracker injection is protected and disabled to comply with BitTorrent private tracker rules (BEP 27). Attached private trackers and health metrics are displayed below.
                      </div>
                    </div>
                  )}

                  {/* Scrape Results Overview */}
                  <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(130px, 1fr))", gap: "0.75rem", marginBottom: "1.25rem" }}>
                    <div className="stat-card" style={{ padding: "0.75rem" }}>
                      <div className="stat-value" style={{ fontSize: "1.25rem" }}>{inspection?.attachedTrackersCount ?? 0}</div>
                      <div className="stat-label" style={{ fontSize: "0.75rem" }}>Attached Trackers</div>
                    </div>
                    <div className="stat-card" style={{ padding: "0.75rem" }}>
                      <div className="stat-value" style={{ fontSize: "1.25rem", color: "var(--success)" }}>
                        {inspection?.verifiedTrackersCount ?? 0}
                      </div>
                      <div className="stat-label" style={{ fontSize: "0.75rem" }}>Verified Candidates</div>
                    </div>
                    <div className="stat-card" style={{ padding: "0.75rem" }}>
                      <div className="stat-value" style={{ fontSize: "1.25rem" }}>{inspection?.totalTrackersChecked ?? 0}</div>
                      <div className="stat-label" style={{ fontSize: "0.75rem" }}>Total Checked</div>
                    </div>
                  </div>

                  {/* Candidate Trackers Table */}
                  {inspectionLoading ? (
                    <div style={{ padding: "3rem", textAlign: "center", color: "var(--text-muted)" }}>
                      Scraping candidate trackers for hash {selectedItem.infoHash.slice(0, 8)}...
                    </div>
                  ) : (
                    <div className="torrent-table-wrapper" style={{ borderRadius: "6px", border: "1px solid var(--border)" }}>
                      <table className="torrent-table" style={{ width: "100%" }}>
                        <thead>
                          <tr>
                            <th className="torrent-table-th" style={{ width: "35%" }}>Tracker URL</th>
                            <th className="torrent-table-th" style={{ width: "10%" }}>Protocol</th>
                            <th className="torrent-table-th" style={{ width: "10%" }}>Latency</th>
                            <th className="torrent-table-th" style={{ width: "25%" }}>Status / Detection</th>
                            <th className="torrent-table-th" style={{ width: "15%" }}>Peers</th>
                            <th className="torrent-table-th" style={{ textAlign: "right" }}>Action</th>
                          </tr>
                        </thead>
                        <tbody>
                          {(inspection?.detections ?? []).map((det) => (
                            <tr key={det.trackerId || det.trackerUrl} className="torrent-table-row" style={{ opacity: det.healthStatus === "Offline" || det.healthStatus === 3 ? 0.6 : 1 }}>
                              <td style={{ maxWidth: "260px", wordBreak: "break-all", fontFamily: "monospace", fontSize: "0.8rem" }}>
                                {det.trackerUrl}
                              </td>
                              <td>
                                <span className="badge badge-secondary" style={{ fontSize: "0.75rem" }}>
                                  {det.protocol}
                                </span>
                              </td>
                              <td style={{ fontFamily: "monospace" }}>{det.latencyMs > 0 ? `${det.latencyMs}ms` : "-"}</td>
                              <td>
                                {det.isAttached ? (
                                  <span className="badge badge-primary" style={{ fontSize: "0.75rem" }}>Attached</span>
                                ) : det.isVerified ? (
                                  <span className="badge badge-success" style={{ fontSize: "0.75rem" }}>✓ Verified Match</span>
                                ) : (
                                  <span className="badge badge-secondary" style={{ fontSize: "0.75rem" }}>{det.detectionStatus}</span>
                                )}
                              </td>
                              <td>
                                <span style={{ color: det.seeders > 0 ? "var(--success)" : "inherit", fontWeight: 600 }}>
                                  {det.seeders} seeds
                                </span>{" "}
                                / <span style={{ color: det.leechers > 0 ? "var(--accent)" : "inherit" }}>{det.leechers} leeches</span>
                              </td>
                              <td style={{ textAlign: "right" }}>
                                {!det.isAttached && det.isVerified && !selectedItem.isPrivate && (
                                  <button
                                    className="btn btn-sm btn-primary"
                                    onClick={() => handleInjectSingle(det.trackerUrl)}
                                    title="Inject this verified tracker into the torrent"
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
                  )}
                </div>
              ) : (
                <div style={{ padding: "4rem", textAlign: "center", color: "var(--text-muted)" }}>
                  Select a download from the left list to inspect live tracker scrape results.
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* TAB 2: SWARM CROSS-MATRIX */}
      {activeTab === "matrix" && (
        <div className="card" style={{ padding: "1.25rem" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "1rem", marginBottom: "1.25rem" }}>
            <div>
              <h2 style={{ fontSize: "1.1rem", margin: "0 0 0.25rem 0" }}>Swarm Cross-Matrix Explorer</h2>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                Bi-directional mapping between library torrents and verified BitTorrent tracker endpoints
              </div>
            </div>

            <div className="view-toggle">
              <button
                className={`view-toggle-btn ${matrixViewMode === "by_torrent" ? "active" : ""}`}
                onClick={() => setMatrixViewMode("by_torrent")}
              >
                Torrents → Trackers
              </button>
              <button
                className={`view-toggle-btn ${matrixViewMode === "by_tracker" ? "active" : ""}`}
                onClick={() => setMatrixViewMode("by_tracker")}
              >
                Trackers → Torrents
              </button>
            </div>
          </div>

          {matrixLoading ? (
            <div style={{ padding: "3rem", textAlign: "center", color: "var(--text-muted)" }}>Building swarm cross-matrix...</div>
          ) : matrixViewMode === "by_torrent" ? (
            <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
              {(matrixData?.torrents ?? []).map((t) => (
                <div key={t.torrentId} className="card" style={{ padding: "1rem", backgroundColor: "var(--bg-secondary, rgba(255,255,255,0.02))" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem", flexWrap: "wrap", gap: "0.5rem" }}>
                    <div>
                      <span style={{ fontWeight: 600, fontSize: "0.95rem" }}>{t.torrentName}</span>
                      {t.isPrivate && (
                        <span className="badge badge-secondary" style={{ marginLeft: "0.5rem", fontSize: "0.7rem" }}>
                          🔒 Private
                        </span>
                      )}
                      <span style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginLeft: "0.75rem", fontFamily: "monospace" }}>
                        {t.infoHash}
                      </span>
                    </div>
                    <div style={{ display: "flex", gap: "0.5rem" }}>
                      <span className="badge badge-primary">{t.attachedTrackersCount} Attached</span>
                      <span className="badge badge-success">{t.verifiedTrackersCount} Verified Matches</span>
                    </div>
                  </div>

                  <div style={{ display: "flex", flexWrap: "wrap", gap: "0.5rem", marginTop: "0.5rem" }}>
                    {t.trackers.map((tr, idx) => (
                      <span
                        key={tr.trackerId || idx}
                        className={`badge ${tr.isAttached ? "badge-primary" : "badge-success"}`}
                        style={{ padding: "0.35rem 0.6rem", fontSize: "0.75rem", fontFamily: "monospace" }}
                      >
                        {tr.trackerHost || tr.trackerUrl} ({tr.seeders}s / {tr.leechers}l)
                      </span>
                    ))}
                    {t.trackers.length === 0 && (
                      <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>No positive tracker scrapes found yet.</span>
                    )}
                  </div>
                </div>
              ))}
              {(matrixData?.torrents ?? []).length === 0 && (
                <div style={{ padding: "3rem", textAlign: "center", color: "var(--text-muted)" }}>
                  No library torrents found.
                </div>
              )}
            </div>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
              {(matrixData?.trackers ?? []).map((tr) => (
                <div key={tr.trackerId} className="card" style={{ padding: "1rem", backgroundColor: "var(--bg-secondary, rgba(255,255,255,0.02))" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem" }}>
                    <div>
                      <span style={{ fontWeight: 600, fontSize: "0.95rem", fontFamily: "monospace" }}>{tr.trackerUrl}</span>
                      <span className="badge badge-secondary" style={{ marginLeft: "0.75rem", fontSize: "0.75rem" }}>{tr.protocol}</span>
                    </div>
                    <span className="badge badge-success">{tr.registeredTorrentsCount} Library Torrents Matched</span>
                  </div>

                  <div style={{ display: "flex", flexWrap: "wrap", gap: "0.5rem", marginTop: "0.5rem" }}>
                    {tr.registeredTorrentNames.map((name, idx) => (
                      <span key={idx} className="badge badge-secondary" style={{ padding: "0.35rem 0.6rem", fontSize: "0.75rem" }}>
                        ✓ {name}
                      </span>
                    ))}
                    {tr.registeredTorrentNames.length === 0 && (
                      <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>No library torrents currently registered on this tracker endpoint.</span>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* TAB 3: TRACKER RADAR */}
      {activeTab === "radar" && (
        <div className="card" style={{ padding: "1.25rem" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "1rem", marginBottom: "1rem" }}>
            <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
              <input
                type="text"
                className="form-control"
                style={{ width: "240px", padding: "0.4rem 0.75rem", fontSize: "0.85rem" }}
                placeholder="Search tracker hosts..."
                value={trackerSearch}
                onChange={(e) => setTrackerSearch(e.target.value)}
              />
              <select
                className="form-control"
                style={{ width: "160px", padding: "0.4rem 0.75rem", fontSize: "0.85rem" }}
                value={sourceFilter}
                onChange={(e) => setSourceFilter(e.target.value)}
              >
                <option value="all">All Sources</option>
                <option value="active">Active Swarm Harvest</option>
                <option value="prowlarr">Prowlarr</option>
                <option value="feeds">Public Feeds</option>
                <option value="manual">Manual Entry</option>
              </select>
            </div>

            <button className="btn btn-primary" onClick={() => setIsAddingTracker(true)}>
              + Add Custom Tracker
            </button>
          </div>

          {isAddingTracker && (
            <form onSubmit={handleAddCustomTracker} style={{ display: "flex", gap: "0.5rem", marginBottom: "1rem" }}>
              <input
                type="text"
                className="form-control"
                placeholder="udp://tracker.example.com:1337/announce"
                value={newTrackerUrl}
                onChange={(e) => setNewTrackerUrl(e.target.value)}
                style={{ flex: 1 }}
              />
              <button type="submit" className="btn btn-primary" disabled={addTracker.isPending}>
                Save
              </button>
              <button type="button" className="btn btn-outline" onClick={() => setIsAddingTracker(false)}>
                Cancel
              </button>
            </form>
          )}

          <div className="torrent-table-wrapper" style={{ borderRadius: "6px", border: "1px solid var(--border)", marginTop: "0.5rem" }}>
            <table className="torrent-table" style={{ width: "100%" }}>
              <thead>
                <tr>
                  <th className="torrent-table-th" style={{ width: "38%" }}>Tracker Endpoint</th>
                  <th className="torrent-table-th" style={{ width: "10%" }}>Protocol</th>
                  <th className="torrent-table-th" style={{ width: "16%" }}>Source</th>
                  <th className="torrent-table-th" style={{ width: "12%" }}>Status</th>
                  <th className="torrent-table-th" style={{ width: "10%" }}>Latency</th>
                  <th className="torrent-table-th" style={{ width: "14%" }}>Verified Swarms</th>
                  <th className="torrent-table-th" style={{ width: "10%", textAlign: "right" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredTrackers.map((tr) => (
                  <tr key={tr.id} className="torrent-table-row">
                    <td style={{ fontFamily: "monospace", fontSize: "0.82rem", wordBreak: "break-all" }}>{tr.url}</td>
                    <td><span className="badge badge-secondary" style={{ fontSize: "0.75rem" }}>{tr.protocol}</span></td>
                    <td><span className="badge badge-outline" style={{ fontSize: "0.75rem" }}>{tr.sourceName}</span></td>
                    <td>
                      <span className={`badge ${tr.status === "Alive" || tr.status === 1 ? "badge-success" : tr.status === "Offline" || tr.status === 3 ? "badge-danger" : "badge-secondary"}`} style={{ fontSize: "0.75rem" }}>
                        {tr.status}
                      </span>
                    </td>
                    <td style={{ fontFamily: "monospace" }}>{tr.latencyMs > 0 ? `${tr.latencyMs}ms` : "-"}</td>
                    <td>{tr.totalVerifiedTorrents ?? tr.totalSwarmsFound} swarms</td>
                    <td style={{ textAlign: "right" }}>
                      <button
                        className="btn btn-sm btn-danger"
                        style={{ padding: "0.25rem 0.6rem", fontSize: "0.75rem" }}
                        onClick={() => deleteTracker.mutate(tr.id)}
                      >
                        Delete
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 4: SOURCES & AUTOMATION SETTINGS */}
      {activeTab === "settings" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          {/* Automation Toggles */}
          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>⚡ Automation & Background Optimization</h3>
            <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 1rem 0" }}>
              TrackerBoost runs as a background service to constantly discover new trackers, monitor health, and optimize swarms across Seedarr and connected download clients.
            </p>

            <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
              <label style={{ display: "flex", alignItems: "center", gap: "0.75rem", cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={settings?.autoBoostEnabled ?? true}
                  onChange={() => handleToggleSetting("autoBoostEnabled")}
                  style={{ width: "1.1rem", height: "1.1rem" }}
                />
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>Automatic Background Swarm Boosting (Enabled by Default)</div>
                  <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                    Periodically queries candidate trackers and automatically injects verified positive matches into active downloads.
                  </div>
                </div>
              </label>

              <label style={{ display: "flex", alignItems: "center", gap: "0.75rem", cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={settings?.autoHarvestEnabled ?? true}
                  onChange={() => handleToggleSetting("autoHarvestEnabled")}
                  style={{ width: "1.1rem", height: "1.1rem" }}
                />
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>Automatic Swarm Tracker Harvesting (Enabled by Default)</div>
                  <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                    Continuously extracts and catalogues new public tracker endpoints from downloading torrents to grow the tracker database.
                  </div>
                </div>
              </label>

              <label style={{ display: "flex", alignItems: "center", gap: "0.75rem", cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={settings?.onlyVerified ?? true}
                  onChange={() => handleToggleSetting("onlyVerified")}
                  style={{ width: "1.1rem", height: "1.1rem" }}
                />
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.9rem" }}>Scrape Verification Guard (Strict Mode)</div>
                  <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                    Only injects trackers that respond with active seeders or leechers for the specific info_hash, preventing client clutter.
                  </div>
                </div>
              </label>
            </div>
          </div>

          {/* Connected Download Agents */}
          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>Connected Download Agents</h3>
            <p style={{ fontSize: "0.85rem", color: "var(--text-muted)", margin: "0 0 1rem 0" }}>
              TrackerBoost coordinates with your download clients (qBittorrent, Transmission, Deluge) to inject verified trackers into active physical downloads.
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
                <span style={{ fontSize: "0.85rem", color: "var(--warning)" }}>
                  No download agents currently configured. Add qBittorrent or Transmission in Settings ⚙️ to boost real downloads.
                </span>
              )}
            </div>
          </div>

          {/* Discovery Feeds */}
          <div className="card" style={{ padding: "1.25rem" }}>
            <h3 style={{ margin: "0 0 0.5rem 0" }}>Manual Discovery Triggers</h3>
            <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap", marginTop: "0.75rem" }}>
              <button
                className="btn btn-outline"
                onClick={handleHarvestDownloads}
                disabled={harvestDownloads.isPending}
              >
                {harvestDownloads.isPending ? "Harvesting..." : "🔄 Harvest Live Swarms"}
              </button>
              <button
                className="btn btn-outline"
                onClick={handleHarvestProwlarr}
                disabled={harvestProwlarr.isPending}
              >
                {harvestProwlarr.isPending ? "Syncing..." : "🔄 Sync Prowlarr Trackers"}
              </button>
              <button
                className="btn btn-outline"
                onClick={handleHarvestFeeds}
                disabled={harvestFeeds.isPending}
              >
                {harvestFeeds.isPending ? "Syncing..." : "🌐 Sync Curated Feeds"}
              </button>
              <button
                className="btn btn-outline"
                onClick={handleScanAll}
                disabled={scanTrackers.isPending}
              >
                {scanTrackers.isPending ? "Probing..." : "📡 Probe All Trackers"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default TrackerBoost;
