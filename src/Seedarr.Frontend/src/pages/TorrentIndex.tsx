import { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router";
import TorrentTable from "../components/TorrentTable";
import TorrentGrid from "../components/TorrentGrid";
import TorrentDetailPanel from "../components/TorrentDetailPanel";
import AddTorrentModal from "../components/AddTorrentModal";
import { QuickSettingsDrawer } from "../components/quicksettings";
import { TorrentToolbar } from "./torrentindex/TorrentToolbar";
import { TorrentFilterPanel } from "./torrentindex/TorrentFilterPanel";
import { useTorrentIndexState } from "./torrentindex/useTorrentIndexState";
import { useAnnounceTorrent, useRecheckTorrent } from "../api/hooks";

function TorrentIndex() {
  const navigate = useNavigate();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const {
    torrents,
    startSeeding,
    stopSeeding,
    deleteTorrent,
    startAll,
    stopAll,
    seedingConfig,
    filter,
    setFilter,
    showAddModal,
    setShowAddModal,
    selectedIds,
    setSelectedIds,
    viewMode,
    selectedState,
    setSelectedState,
    selectedTracker,
    setSelectedTracker,
    selectedTorrentId,
    setSelectedTorrentId,
    adjustSpeed,
    stateCounts,
    trackerGroups,
    totalUploadSpeed,
    totalDownloadSpeed,
    handleViewMode,
    handleToggleSelect,
    handleSelectAll,
    isFilterCollapsed,
    toggleFilterCollapse,
    isQuickControlsOpen,
    toggleQuickControls,
    closeQuickControls,
  } = useTorrentIndexState();

  const [bulkPending, setBulkPending] = useState(false);

  const handleBulkStart = useCallback(async () => {
    setBulkPending(true);
    try {
      await Promise.all(
        [...selectedIds].map((id) => startSeeding.mutateAsync(id)),
      );
    } finally {
      setBulkPending(false);
      setSelectedIds(new Set());
    }
  }, [selectedIds, startSeeding, setSelectedIds]);

  const handleBulkStop = useCallback(async () => {
    setBulkPending(true);
    try {
      await Promise.all(
        [...selectedIds].map((id) => stopSeeding.mutateAsync(id)),
      );
    } finally {
      setBulkPending(false);
      setSelectedIds(new Set());
    }
  }, [selectedIds, stopSeeding, setSelectedIds]);

  const handleBulkDelete = useCallback(async () => {
    if (!confirm(`Delete ${selectedIds.size} torrent(s)?`)) return;
    setBulkPending(true);
    try {
      await Promise.all(
        [...selectedIds].map((id) => deleteTorrent.mutateAsync({ id })),
      );
    } finally {
      setBulkPending(false);
      setSelectedIds(new Set());
    }
  }, [selectedIds, deleteTorrent, setSelectedIds]);

  // Keyboard Shortcuts Listener for Torrent Operations, Navigation & Modals
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (
        e.target instanceof HTMLInputElement ||
        e.target instanceof HTMLTextAreaElement ||
        e.target instanceof HTMLSelectElement ||
        (e.target as HTMLElement)?.isContentEditable
      ) {
        return;
      }

      // 'q' / 'Q' toggles quick controls drawer
      if (e.key === "q" || e.key === "Q") {
        e.preventDefault();
        toggleQuickControls();
        return;
      }

      // 'Escape' closes detail panel or quick controls drawer
      if (e.key === "Escape") {
        if (isQuickControlsOpen) {
          e.preventDefault();
          closeQuickControls();
          return;
        }
        if (selectedTorrentId != null) {
          e.preventDefault();
          setSelectedTorrentId(null);
          return;
        }
      }

      // Hotkeys for selected torrent
      if (selectedTorrentId != null) {
        const targetTorrent = torrents?.find((t) => t.id === selectedTorrentId);

        // Space or p / P: pause / resume
        if (e.key === " " || e.key === "p" || e.key === "P") {
          e.preventDefault();
          if (targetTorrent?.status === "Seeding") {
            stopSeeding.mutate(selectedTorrentId);
          } else {
            startSeeding.mutate(selectedTorrentId);
          }
          return;
        }

        // a / A: force announce to all trackers
        if (e.key === "a" || e.key === "A") {
          e.preventDefault();
          announceTorrent.mutate(selectedTorrentId);
          return;
        }

        // r / R: force recheck torrent files
        if (e.key === "r" || e.key === "R") {
          e.preventDefault();
          recheckTorrent.mutate(selectedTorrentId);
          return;
        }

        // Delete: delete torrent
        if (e.key === "Delete") {
          e.preventDefault();
          if (
            confirm(
              `Delete torrent "${targetTorrent?.name || selectedTorrentId}"?`,
            )
          ) {
            deleteTorrent.mutate({ id: selectedTorrentId });
            setSelectedTorrentId(null);
          }
          return;
        }
      }

      // Bulk actions when multiple items are selected via table checkboxes
      if (selectedIds.size > 0 && selectedTorrentId == null) {
        if (e.key === "Delete") {
          e.preventDefault();
          handleBulkDelete();
          return;
        }
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [
    toggleQuickControls,
    closeQuickControls,
    isQuickControlsOpen,
    selectedTorrentId,
    setSelectedTorrentId,
    selectedIds,
    torrents,
    startSeeding,
    stopSeeding,
    announceTorrent,
    recheckTorrent,
    deleteTorrent,
    handleBulkDelete,
  ]);

  const count = torrents?.length ?? 0;

  return (
    <div className="torrent-index-page">
      <TorrentToolbar
        count={count}
        totalUploadSpeed={totalUploadSpeed}
        totalDownloadSpeed={totalDownloadSpeed}
        seedingConfig={seedingConfig}
        adjustSpeed={adjustSpeed}
        filter={filter}
        onFilterChange={setFilter}
        viewMode={viewMode}
        onViewModeChange={handleViewMode}
        onAddTorrent={() => setShowAddModal(true)}
        onSearchIndexers={() => navigate("/settings/indexers")}
        onStartAll={() => startAll.mutate()}
        onStopAll={() => stopAll.mutate()}
        selectedCount={selectedIds.size}
        bulkPending={bulkPending}
        onBulkStart={handleBulkStart}
        onBulkStop={handleBulkStop}
        onBulkDelete={handleBulkDelete}
        onBulkClear={() => setSelectedIds(new Set())}
        isFilterCollapsed={isFilterCollapsed}
        onToggleFilter={toggleFilterCollapse}
        isQuickControlsOpen={isQuickControlsOpen}
        onToggleQuickControls={toggleQuickControls}
      />
      <QuickSettingsDrawer
        isOpen={isQuickControlsOpen}
        onClose={closeQuickControls}
      />
      <div className="torrent-content-layout">
        <TorrentFilterPanel
          selectedState={selectedState}
          onSelectState={setSelectedState}
          selectedTracker={selectedTracker}
          onSelectTracker={setSelectedTracker}
          stateCounts={stateCounts}
          trackerGroups={trackerGroups}
          count={count}
          isCollapsed={isFilterCollapsed}
          onToggleCollapse={toggleFilterCollapse}
        />
        <div className="filter-content">
          <div className="torrent-split-pane">
            <div className="torrent-split-top">
              {viewMode === "table" ? (
                <TorrentTable
                  filter={filter}
                  stateFilter={selectedState}
                  trackerFilter={selectedTracker}
                  selectedTorrentId={selectedTorrentId}
                  onSelectTorrent={setSelectedTorrentId}
                  selectedIds={selectedIds}
                  onToggleSelect={handleToggleSelect}
                  onSelectAll={handleSelectAll}
                />
              ) : (
                <TorrentGrid
                  filter={filter}
                  stateFilter={selectedState}
                  trackerFilter={selectedTracker}
                  selectedTorrentId={selectedTorrentId}
                  onSelectTorrent={setSelectedTorrentId}
                />
              )}
            </div>
            {selectedTorrentId != null && (
              <TorrentDetailPanel
                torrentId={selectedTorrentId}
                onClose={() => setSelectedTorrentId(null)}
              />
            )}
          </div>
        </div>
      </div>
      {showAddModal && (
        <AddTorrentModal onClose={() => setShowAddModal(false)} />
      )}
    </div>
  );
}

export default TorrentIndex;
