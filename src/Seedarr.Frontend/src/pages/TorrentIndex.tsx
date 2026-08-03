import { useState } from "react";
import TorrentTable from "../components/TorrentTable";
import TorrentGrid from "../components/TorrentGrid";
import TorrentDetailPanel from "../components/TorrentDetailPanel";
import AddTorrentModal from "../components/AddTorrentModal";
import { TorrentToolbar } from "./torrentindex/TorrentToolbar";
import { TorrentFilterPanel } from "./torrentindex/TorrentFilterPanel";
import { useTorrentIndexState } from "./torrentindex/useTorrentIndexState";

function TorrentIndex() {
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
  } = useTorrentIndexState();

  const [bulkPending, setBulkPending] = useState(false);

  async function handleBulkStart() {
    setBulkPending(true);
    try {
      await Promise.all(
        [...selectedIds].map((id) => startSeeding.mutateAsync(id)),
      );
    } finally {
      setBulkPending(false);
      setSelectedIds(new Set());
    }
  }

  async function handleBulkStop() {
    setBulkPending(true);
    try {
      await Promise.all(
        [...selectedIds].map((id) => stopSeeding.mutateAsync(id)),
      );
    } finally {
      setBulkPending(false);
      setSelectedIds(new Set());
    }
  }

  async function handleBulkDelete() {
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
  }

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
        onStartAll={() => startAll.mutate()}
        onStopAll={() => stopAll.mutate()}
        selectedCount={selectedIds.size}
        bulkPending={bulkPending}
        onBulkStart={handleBulkStart}
        onBulkStop={handleBulkStop}
        onBulkDelete={handleBulkDelete}
        onBulkClear={() => setSelectedIds(new Set())}
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
