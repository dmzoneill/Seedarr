import { useState, useMemo } from "react";
import { Torrent } from "../../api/types";
import {
  useTorrentTrackers,
  useTrackerBoostTrackers,
  useInspectTorrentTrackers,
  useAddTorrentTracker,
  useDeleteTorrentTracker,
  useBoostTorrent,
  useAnnounceTorrentTracker,
} from "../../api/hooks";
import { formatDate } from "../../utils/formatters";
import { SkeletonLine } from "../../components/Skeleton";
import { useToast } from "../../context/ToastContext";
import TrackerFavicon from "../../components/TrackerFavicon";
import TrackerMultiSelectModal, {
  TrackerPickerItem,
} from "../../components/TrackerMultiSelectModal";

function getAttachedTrackerIndicator(
  status: string,
  det?: { isVerified?: boolean; healthStatus?: string | number },
): {
  icon: string;
  badgeClass: string;
} {
  const isWorking =
    status === "Working" ||
    status === "Announcing" ||
    det?.isVerified ||
    det?.healthStatus === "Alive" ||
    det?.healthStatus === 1;
  const isFailed =
    status === "Failed" ||
    status === "Disabled" ||
    det?.healthStatus === "Offline" ||
    det?.healthStatus === 3;
  const isSlow = det?.healthStatus === "Slow" || det?.healthStatus === 2;

  if (isWorking) {
    return {
      icon: "🟢",
      badgeClass:
        status === "Announcing" ? "badge-announcing" : "badge-seeding",
    };
  }
  if (isFailed) {
    return {
      icon: "🔴",
      badgeClass: "badge-error",
    };
  }
  if (isSlow) {
    return {
      icon: "🟡",
      badgeClass: "badge-warning",
    };
  }
  return {
    icon: "🟡",
    badgeClass: "badge-warning",
  };
}

export function TrackersTab({ torrent }: { torrent: Torrent }) {
  const {
    data: trackers,
    isLoading,
    error,
    refetch,
  } = useTorrentTrackers(torrent.id);
  const { data: availableTrackers } = useTrackerBoostTrackers();
  const { data: inspection } = useInspectTorrentTrackers(
    torrent.id,
    torrent.id > 0,
  );
  const addTracker = useAddTorrentTracker();
  const deleteTracker = useDeleteTorrentTracker();
  const boostTorrent = useBoostTorrent();
  const announceTracker = useAnnounceTorrentTracker();
  const { showToast } = useToast();
  const [showPickerModal, setShowPickerModal] = useState(false);
  const [selectedUrls, setSelectedUrls] = useState<Set<string>>(new Set());
  const [isAddingBatch, setIsAddingBatch] = useState(false);

  const attachedUrls = useMemo(() => {
    return new Set(
      (trackers ?? []).map((t) => (t.url ?? "").trim().toLowerCase()),
    );
  }, [trackers]);

  const detectionMap = useMemo(() => {
    const map = new Map<
      string,
      NonNullable<typeof inspection>["detections"][number]
    >();
    (inspection?.detections ?? []).forEach((d) => {
      if (d.trackerUrl) {
        map.set(d.trackerUrl.trim().toLowerCase(), d);
      }
    });
    return map;
  }, [inspection]);

  const pickerTrackers = useMemo<TrackerPickerItem[]>(() => {
    return (availableTrackers ?? []).map((tr) => {
      const cleanUrl = (tr.url ?? "").trim().toLowerCase();
      const det = detectionMap.get(cleanUrl);
      const isAttached = attachedUrls.has(cleanUrl) || det?.isAttached || false;
      const isVerified = det?.isVerified || false;
      const isAlive =
        tr.status === "Alive" ||
        tr.status === 1 ||
        det?.healthStatus === "Alive" ||
        det?.healthStatus === 1 ||
        false;
      const isSlow =
        tr.status === "Slow" ||
        tr.status === 2 ||
        det?.healthStatus === "Slow" ||
        det?.healthStatus === 2 ||
        false;
      const isOffline =
        tr.status === "Offline" ||
        tr.status === 3 ||
        det?.healthStatus === "Offline" ||
        det?.healthStatus === 3 ||
        false;

      let statusLabel = "Untested";
      if (isAttached) {
        statusLabel = "Attached";
      } else if (isVerified) {
        statusLabel = `✓ Found in Swarm (${det?.seeders ?? 0}s / ${det?.leechers ?? 0}l)`;
      } else if (isAlive) {
        statusLabel = "Online (0 Peers)";
      } else if (isSlow) {
        statusLabel = `Slow (${tr.latencyMs > 0 ? tr.latencyMs + "ms" : "High Latency"})`;
      } else if (isOffline) {
        statusLabel = "Offline";
      }

      return {
        url: tr.url,
        host: tr.host,
        protocol: String(tr.protocol),
        isAttached,
        isVerified,
        isAlive,
        isSlow,
        isOffline,
        latencyMs: tr.latencyMs,
        seeders: det?.seeders,
        leechers: det?.leechers,
        statusLabel,
      };
    });
  }, [availableTrackers, detectionMap, attachedUrls]);

  const handleToggleUrl = (url: string) => {
    setSelectedUrls((prev) => {
      const next = new Set(prev);
      if (next.has(url)) next.delete(url);
      else next.add(url);
      return next;
    });
  };

  const handleSelectBatch = (urls: string[]) => {
    setSelectedUrls((prev) => {
      const next = new Set(prev);
      urls.forEach((u) => next.add(u));
      return next;
    });
  };

  const handleClearSelection = () => {
    setSelectedUrls(new Set());
  };

  const handleAddAndAnnounceSelected = async () => {
    if (!torrent.id || selectedUrls.size === 0) return;

    setIsAddingBatch(true);
    let addedCount = 0;
    for (const url of Array.from(selectedUrls)) {
      try {
        await addTracker.mutateAsync({ torrentId: torrent.id, url });
        addedCount++;
      } catch {
        // continue
      }
    }
    setIsAddingBatch(false);
    setSelectedUrls(new Set());
    setShowPickerModal(false);
    showToast(
      `Added ${addedCount} tracker(s) to torrent and triggered announce`,
      "success",
    );
    refetch();
  };

  const handleEnrichTrackers = () => {
    boostTorrent.mutate(torrent.id, {
      onSuccess: (res) => {
        showToast(res.message, res.boosted ? "success" : "info");
        refetch();
      },
      onError: (err) => {
        showToast(`Failed to enrich trackers: ${err.message}`, "error");
      },
    });
  };

  const handleDeleteTracker = (trackerId: number) => {
    deleteTracker.mutate(
      { torrentId: torrent.id, trackerId },
      {
        onSuccess: () => {
          showToast("Tracker removed and reannounced", "success");
          refetch();
        },
        onError: (err) => {
          showToast(`Failed to remove tracker: ${err.message}`, "error");
        },
      },
    );
  };

  return (
    <div className="card">
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "0.75rem",
        }}
      >
        <h3 style={{ margin: 0 }}>Trackers</h3>
        {!torrent.isPrivate && (
          <button
            className="btn btn-sm btn-primary"
            onClick={handleEnrichTrackers}
            disabled={boostTorrent.isPending}
            title="Query candidate trackers via BEP 15/48 scrape and inject verified seeders"
          >
            {boostTorrent.isPending
              ? "Scraping & Enriching..."
              : "⚡ Enrich Trackers (Tracker Boost)"}
          </button>
        )}
      </div>
      {isLoading && (
        <div className="torrent-table-wrapper">
          <SkeletonLine width="100%" height="2rem" />
          <SkeletonLine width="100%" height="1.5rem" />
          <SkeletonLine width="100%" height="1.5rem" />
        </div>
      )}
      {error && <p className="error">Failed to load trackers.</p>}
      {trackers && trackers.length === 0 && (
        <p className="torrent-table-empty">No trackers configured</p>
      )}
      {trackers && trackers.length > 0 && (
        <div className="torrent-table-wrapper">
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th">URL</th>
                <th className="torrent-table-th">Tier</th>
                <th className="torrent-table-th">Status</th>
                <th className="torrent-table-th">Seeders</th>
                <th className="torrent-table-th">Leechers</th>
                <th className="torrent-table-th">Announces</th>
                <th className="torrent-table-th">Last Announce</th>
                <th className="torrent-table-th">Next Announce</th>
                <th
                  className="torrent-table-th"
                  style={{ textAlign: "right", width: "90px" }}
                >
                  Action
                </th>
              </tr>
            </thead>
            <tbody>
              {trackers.map((tracker) => {
                const det = detectionMap.get(
                  (tracker.url ?? "").trim().toLowerCase(),
                );
                const ind = getAttachedTrackerIndicator(tracker.status, det);
                return (
                  <tr key={tracker.id} className="torrent-table-row">
                    <td className="mono" style={{ wordBreak: "break-all" }}>
                      <div
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.4rem",
                        }}
                      >
                        <TrackerFavicon urlOrHost={tracker.url} size={15} />
                        <span>{tracker.url}</span>
                      </div>
                    </td>
                    <td>{tracker.tier}</td>
                    <td>
                      <span
                        className={`badge ${ind.badgeClass}`}
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.35rem",
                        }}
                      >
                        <span style={{ fontSize: "0.85em" }}>{ind.icon}</span>
                        <span>{tracker.status}</span>
                      </span>
                    </td>
                    <td>{tracker.seeders}</td>
                    <td>{tracker.leechers}</td>
                    <td>
                      {tracker.successfulAnnounces}/{tracker.totalAnnounces}
                    </td>
                    <td>{formatDate(tracker.lastAnnounce)}</td>
                    <td>{formatDate(tracker.nextAnnounce)}</td>
                    <td style={{ textAlign: "right" }}>
                      <div style={{ display: "inline-flex", gap: "0.35rem", justifyContent: "flex-end" }}>
                        <button
                          className="btn btn-sm btn-primary"
                          style={{
                            padding: "0.2rem 0.5rem",
                            fontSize: "0.75rem",
                          }}
                          onClick={() => {
                            announceTracker.mutate(
                              { torrentId: torrent.id, trackerId: tracker.id },
                              {
                                onSuccess: (data) => {
                                  showToast(data.message || "Announce queued", "success");
                                },
                                onError: (err) => {
                                  showToast(`Announce failed: ${err.message}`, "error");
                                },
                              },
                            );
                          }}
                          disabled={announceTracker.isPending}
                          title="Trigger immediate tracker announce"
                        >
                          {announceTracker.isPending ? "..." : "Announce"}
                        </button>
                        <button
                          className="btn btn-sm btn-danger"
                          style={{
                            padding: "0.2rem 0.5rem",
                            fontSize: "0.75rem",
                          }}
                          onClick={() => handleDeleteTracker(tracker.id)}
                          disabled={deleteTracker.isPending}
                          title="Remove tracker from torrent and reannounce"
                        >
                          Remove
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Action bar below listed trackers */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: "0.5rem",
          padding: "0.75rem 0 0.25rem 0",
          marginTop: "0.75rem",
          borderTop: "1px solid var(--border)",
          flexWrap: "wrap",
        }}
      >
        <label
          style={{
            fontSize: "0.85rem",
            fontWeight: 500,
            color: "var(--text-secondary)",
          }}
        >
          Add Tracker:
        </label>
        <button
          type="button"
          className="form-control btn-action"
          style={{
            flex: "1 1 280px",
            maxWidth: "520px",
            padding: "0.35rem 0.75rem",
            fontSize: "0.85rem",
            textAlign: "left",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            cursor: "pointer",
          }}
          onClick={() => setShowPickerModal(true)}
          title="Open tracker picker to select, search, and filter trackers"
        >
          <span
            style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
          >
            <span>🎯</span>
            {selectedUrls.size === 0 ? (
              <span style={{ color: "var(--text-muted)" }}>
                Choose Trackers to Add... (0 Selected)
              </span>
            ) : (
              <span style={{ color: "var(--text-primary)", fontWeight: 600 }}>
                {selectedUrls.size} Tracker{selectedUrls.size === 1 ? "" : "s"}{" "}
                Selected (Click to change)
              </span>
            )}
          </span>

          <span
            className={`badge ${selectedUrls.size > 0 ? "badge-success" : "badge-secondary"}`}
            style={{ fontSize: "0.7rem", padding: "0.15rem 0.45rem" }}
          >
            {selectedUrls.size} Selected
          </span>
        </button>

        <button
          className="btn btn-sm btn-primary"
          onClick={handleAddAndAnnounceSelected}
          disabled={
            isAddingBatch ||
            selectedUrls.size === 0 ||
            !availableTrackers ||
            availableTrackers.length === 0
          }
          title="Add selected tracker(s) to this torrent and trigger announce"
        >
          {isAddingBatch
            ? "Adding..."
            : selectedUrls.size > 0
              ? `+ Add & Announce (${selectedUrls.size})`
              : "+ Add & Announce"}
        </button>
      </div>

      <TrackerMultiSelectModal
        isOpen={showPickerModal}
        onClose={() => setShowPickerModal(false)}
        trackers={pickerTrackers}
        selectedUrls={selectedUrls}
        onToggleUrl={handleToggleUrl}
        onSelectBatch={handleSelectBatch}
        onClearSelection={handleClearSelection}
        onAddAndAnnounce={handleAddAndAnnounceSelected}
        isAdding={isAddingBatch}
      />
    </div>
  );
}
