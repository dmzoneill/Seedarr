import { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router";
import TorrentTable from "../components/TorrentTable";
import TorrentGrid from "../components/TorrentGrid";
import TorrentDetailPanel from "../components/TorrentDetailPanel";
import AddTorrentModal from "../components/AddTorrentModal";
import DeleteTorrentModal from "../components/DeleteTorrentModal";
import { QuickSettingsDrawer } from "../components/quicksettings";
import { TorrentToolbar } from "./torrentindex/TorrentToolbar";
import { TorrentFilterPanel } from "./torrentindex/TorrentFilterPanel";
import { useTorrentIndexState } from "./torrentindex/useTorrentIndexState";
import { useColumnPreferences } from "./torrentindex/columnPreferences";
import {
  useAnnounceTorrent,
  useRecheckTorrent,
  useBulkTorrentAction,
  useMoveTorrentQueue,
} from "../api/hooks";
import { useToast } from "../context/ToastContext";
import { useModalStack } from "../components/ModalProvider";

function TorrentIndex() {
  const navigate = useNavigate();
  const { showToast } = useToast();
  const announceTorrent = useAnnounceTorrent();
  const recheckTorrent = useRecheckTorrent();
  const bulkAction = useBulkTorrentAction();
  const { modalCount } = useModalStack();
  const {
    visibleColumns,
    toggleColumn,
    resetToDefaults: resetColumns,
    resetSort,
    selectAll: selectAllColumns,
    deselectAll: deselectAllColumns,
    applyPreset: applyColumnPreset,
    toggleCategory: toggleCategoryColumns,
  } = useColumnPreferences();
  const {
    torrents,
    filteredTorrents,
    startSeeding,
    stopSeeding,
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
    selectedCategory,
    setSelectedCategory,
    selectedTag,
    setSelectedTag,
    selectedTagIds,
    toggleTag,
    clearTags,
    tagMatchMode,
    setTagMatchMode,
    untaggedCount,
    selectedTorrentId,
    setSelectedTorrentId,
    adjustSpeed,
    stateCounts,
    trackerGroups,
    categoryGroups,
    tagGroups,
    totalUploadSpeed,
    totalDownloadSpeed,
    handleViewMode,
    handleToggleSelect,
    handleSelectAll,
    handleSelectRange,
    isFilterCollapsed,
    toggleFilterCollapse,
    isQuickControlsOpen,
    toggleQuickControls,
    closeQuickControls,
  } = useTorrentIndexState();

  const [bulkPending, setBulkPending] = useState(false);
  const [deleteModalState, setDeleteModalState] = useState<{
    isOpen: boolean;
    targetIds: number[];
    torrentName?: string;
  } | null>(null);

  const handleBulkStart = useCallback(async () => {
    if (selectedIds.size === 0) return;
    setBulkPending(true);
    const ids = [...selectedIds];
    try {
      const res = await bulkAction.mutateAsync({
        torrentIds: ids,
        action: "start",
      });
      const succeeded = res.succeededIds ?? [];

      setSelectedIds((prev) => {
        const next = new Set(prev);
        succeeded.forEach((id) => next.delete(id));
        return next;
      });

      if (res.failedCount === 0) {
        showToast(
          `Successfully started ${res.successCount} torrent(s).`,
          "success",
        );
      } else {
        showToast(
          `${res.successCount} started, ${res.failedCount} failed`,
          "warning",
        );
      }
    } catch (err: unknown) {
      const msg =
        err instanceof Error ? err.message : "Failed to start torrents";
      showToast(msg, "error");
    } finally {
      setBulkPending(false);
    }
  }, [selectedIds, bulkAction, setSelectedIds, showToast]);

  const handleBulkStop = useCallback(async () => {
    if (selectedIds.size === 0) return;
    setBulkPending(true);
    const ids = [...selectedIds];
    try {
      const res = await bulkAction.mutateAsync({
        torrentIds: ids,
        action: "stop",
      });
      const succeeded = res.succeededIds ?? [];

      setSelectedIds((prev) => {
        const next = new Set(prev);
        succeeded.forEach((id) => next.delete(id));
        return next;
      });

      if (res.failedCount === 0) {
        showToast(
          `Successfully stopped ${res.successCount} torrent(s).`,
          "success",
        );
      } else {
        showToast(
          `${res.successCount} stopped, ${res.failedCount} failed`,
          "warning",
        );
      }
    } catch (err: unknown) {
      const msg =
        err instanceof Error ? err.message : "Failed to stop torrents";
      showToast(msg, "error");
    } finally {
      setBulkPending(false);
    }
  }, [selectedIds, bulkAction, setSelectedIds, showToast]);

  const handleBulkDelete = useCallback(() => {
    const targetIds =
      selectedIds.size > 0
        ? Array.from(selectedIds)
        : selectedTorrentId != null
          ? [selectedTorrentId]
          : [];
    if (targetIds.length === 0) return;
    const torrentName =
      targetIds.length === 1
        ? (torrents ?? []).find((t) => t.id === targetIds[0])?.name
        : undefined;
    setDeleteModalState({
      isOpen: true,
      targetIds,
      torrentName,
    });
  }, [selectedIds, selectedTorrentId, torrents]);

  const handleConfirmBulkDelete = useCallback(
    async (deleteFiles: boolean) => {
      if (!deleteModalState || deleteModalState.targetIds.length === 0) return;
      const targetIds = deleteModalState.targetIds;
      setBulkPending(true);
      try {
        const res = await bulkAction.mutateAsync({
          torrentIds: targetIds,
          action: "delete",
          deleteFiles,
        });
        const succeeded = res.succeededIds ?? [];

        setSelectedIds((prev) => {
          const next = new Set(prev);
          succeeded.forEach((id) => next.delete(id));
          return next;
        });

        if (
          selectedTorrentId != null &&
          succeeded.includes(selectedTorrentId)
        ) {
          setSelectedTorrentId(null);
        }

        if (res.failedCount === 0) {
          showToast(
            `Successfully deleted ${res.successCount} torrent(s).`,
            "success",
          );
        } else {
          showToast(
            `${res.successCount} deleted, ${res.failedCount} failed`,
            "warning",
          );
        }
        setDeleteModalState(null);
      } catch (err: unknown) {
        const msg =
          err instanceof Error ? err.message : "Failed to delete torrents";
        showToast(msg, "error");
      } finally {
        setBulkPending(false);
      }
    },
    [
      deleteModalState,
      bulkAction,
      selectedTorrentId,
      setSelectedIds,
      setSelectedTorrentId,
      showToast,
    ],
  );

  const handleToggleActiveSelected = useCallback(() => {
    if (selectedIds.size > 0) {
      const selectedTorrents = (torrents ?? []).filter((t) =>
        selectedIds.has(t.id),
      );
      const anyActive = selectedTorrents.some(
        (t) => t.status === "Seeding" || t.active,
      );
      if (anyActive) {
        handleBulkStop();
      } else {
        handleBulkStart();
      }
      return;
    }

    if (selectedTorrentId != null) {
      const targetTorrent = torrents?.find((t) => t.id === selectedTorrentId);
      if (targetTorrent?.status === "Seeding" || targetTorrent?.active) {
        stopSeeding.mutate(selectedTorrentId);
      } else {
        startSeeding.mutate(selectedTorrentId);
      }
      return;
    }
  }, [
    selectedIds,
    torrents,
    handleBulkStop,
    handleBulkStart,
    selectedTorrentId,
    stopSeeding,
    startSeeding,
  ]);

  const moveTorrentQueue = useMoveTorrentQueue();

  const handleBulkMoveQueue = useCallback(
    async (position: "top" | "up" | "down" | "bottom") => {
      const activeIds = new Set((torrents ?? []).map((t) => t.id));
      const validSelectedIds = Array.from(selectedIds).filter((id) =>
        activeIds.has(id),
      );
      if (validSelectedIds.length === 0) return;

      const orderedIds = (torrents ?? [])
        .filter((t) => validSelectedIds.includes(t.id))
        .map((t) => t.id);

      if (position === "down" || position === "bottom") {
        orderedIds.reverse();
      }

      setBulkPending(true);
      try {
        for (const id of orderedIds) {
          await moveTorrentQueue.mutateAsync({ id, position });
        }
      } catch (err) {
        const msg =
          err instanceof Error ? err.message : "Failed to move torrents";
        showToast(msg, "error");
      } finally {
        setBulkPending(false);
      }
    },
    [selectedIds, torrents, moveTorrentQueue, showToast],
  );

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

      // Suppress all page-level shortcuts and Escape processing when any modal is open
      if (
        deleteModalState?.isOpen ||
        showAddModal ||
        modalCount > 0 ||
        (typeof document !== "undefined" &&
          Boolean(
            document.querySelector(
              'dialog[open], [role="dialog"], [aria-modal="true"], .modal-overlay, .modal-backdrop',
            ),
          ))
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

      // Ctrl+A / Cmd+A to select all filtered torrents
      if ((e.ctrlKey || e.metaKey) && (e.key === "a" || e.key === "A")) {
        e.preventDefault();
        handleSelectAll(filteredTorrents.map((t) => t.id));
        return;
      }

      // ArrowUp / ArrowDown navigation across filtered torrents
      if (e.key === "ArrowUp" || e.key === "ArrowDown") {
        if (filteredTorrents.length === 0) return;
        e.preventDefault();
        const currentIndex = filteredTorrents.findIndex(
          (t) => t.id === selectedTorrentId,
        );
        let nextIndex = 0;
        if (e.key === "ArrowUp") {
          nextIndex =
            currentIndex > 0 ? currentIndex - 1 : filteredTorrents.length - 1;
        } else {
          nextIndex =
            currentIndex >= 0 && currentIndex < filteredTorrents.length - 1
              ? currentIndex + 1
              : 0;
        }
        const nextTorrent = filteredTorrents[nextIndex];
        if (nextTorrent) {
          setSelectedTorrentId(nextTorrent.id);
          if (e.shiftKey) {
            handleSelectRange([nextTorrent.id]);
          }
        }
        return;
      }

      // Space or p / P: pause / resume (supporting single & multi-selection)
      if (e.key === " " || e.key === "p" || e.key === "P") {
        e.preventDefault();
        handleToggleActiveSelected();
        return;
      }

      // a / A: force announce to all trackers (single selected)
      if (e.key === "a" || e.key === "A") {
        if (selectedTorrentId != null) {
          e.preventDefault();
          announceTorrent.mutate(selectedTorrentId);
          return;
        }
      }

      // r / R: force recheck torrent files (single selected)
      if (e.key === "r" || e.key === "R") {
        if (selectedTorrentId != null) {
          e.preventDefault();
          recheckTorrent.mutate(selectedTorrentId);
          return;
        }
      }

      // Delete / Backspace: delete torrent (supporting single & multi-selection)
      if (e.key === "Delete" || e.key === "Backspace") {
        e.preventDefault();
        handleBulkDelete();
        return;
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
    filteredTorrents,
    announceTorrent,
    recheckTorrent,
    handleToggleActiveSelected,
    handleBulkDelete,
    handleSelectAll,
    handleSelectRange,
    modalCount,
    deleteModalState?.isOpen,
    showAddModal,
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
        onBulkMoveQueue={handleBulkMoveQueue}
        isFilterCollapsed={isFilterCollapsed}
        onToggleFilter={toggleFilterCollapse}
        isQuickControlsOpen={isQuickControlsOpen}
        onToggleQuickControls={toggleQuickControls}
        visibleColumns={visibleColumns}
        onToggleColumn={toggleColumn}
        onResetColumns={resetColumns}
        onResetSort={resetSort}
        onSelectAllColumns={selectAllColumns}
        onDeselectAllColumns={deselectAllColumns}
        onApplyColumnPreset={applyColumnPreset}
        onToggleCategoryColumns={toggleCategoryColumns}
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
          selectedCategory={selectedCategory}
          onSelectCategory={setSelectedCategory}
          selectedTag={selectedTag}
          onSelectTag={setSelectedTag}
          selectedTagIds={selectedTagIds}
          onToggleTag={toggleTag}
          tagMatchMode={tagMatchMode}
          onTagMatchModeChange={setTagMatchMode}
          onClearTags={clearTags}
          untaggedCount={untaggedCount}
          stateCounts={stateCounts}
          trackerGroups={trackerGroups}
          categoryGroups={categoryGroups}
          tagGroups={tagGroups}
          count={count}
          isCollapsed={isFilterCollapsed}
          onToggleCollapse={toggleFilterCollapse}
        />
        <div className="filter-content">
          <div className="torrent-split-pane">
            <div className="torrent-split-top">
              {viewMode === "table" ? (
                <TorrentTable
                  torrents={filteredTorrents}
                  filter={filter}
                  stateFilter={selectedState}
                  trackerFilter={selectedTracker}
                  categoryFilter={selectedCategory}
                  tagFilter={selectedTag}
                  selectedTagIds={selectedTagIds}
                  tagMatchMode={tagMatchMode}
                  selectedTorrentId={selectedTorrentId}
                  onSelectTorrent={setSelectedTorrentId}
                  selectedIds={selectedIds}
                  onToggleSelect={handleToggleSelect}
                  onSelectAll={handleSelectAll}
                  onSelectRange={handleSelectRange}
                  onSelectMultiple={setSelectedIds}
                  onDeleteSelected={handleBulkDelete}
                  onToggleActive={handleToggleActiveSelected}
                  visibleColumns={visibleColumns}
                  onToggleColumn={toggleColumn}
                />
              ) : (
                <TorrentGrid
                  torrents={filteredTorrents}
                  filter={filter}
                  stateFilter={selectedState}
                  trackerFilter={selectedTracker}
                  categoryFilter={selectedCategory}
                  tagFilter={selectedTag}
                  selectedTagIds={selectedTagIds}
                  tagMatchMode={tagMatchMode}
                  selectedTorrentId={selectedTorrentId}
                  onSelectTorrent={setSelectedTorrentId}
                  selectedIds={selectedIds}
                  onToggleSelect={handleToggleSelect}
                  onSelectAll={() =>
                    handleSelectAll(filteredTorrents.map((t) => t.id))
                  }
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
      {deleteModalState?.isOpen && (
        <DeleteTorrentModal
          isOpen={deleteModalState.isOpen}
          count={deleteModalState.targetIds.length}
          torrentName={deleteModalState.torrentName}
          isPending={bulkPending}
          onClose={() => {
            if (!bulkPending) setDeleteModalState(null);
          }}
          onConfirm={handleConfirmBulkDelete}
        />
      )}
    </div>
  );
}

export default TorrentIndex;
