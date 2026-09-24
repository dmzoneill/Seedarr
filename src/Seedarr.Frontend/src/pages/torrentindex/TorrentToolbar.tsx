import React, { useState, useEffect } from "react";
import { SeedingConfig } from "../../api/types";
import { usePermissions } from "../../hooks/usePermissions";
import { useTranslation } from "../../i18n";
import { TagIcon } from "../../components/icons/NavIcons";
import {
  PlusIcon,
  PlayIcon,
  StopIcon,
  TableIcon,
  GridIcon,
  FilterIcon,
  SlidersIcon,
  ColumnsIcon,
  UploadIcon,
} from "../../components/icons/UIIcons";
import { ViewMode } from "./types";
import { ColumnCustomizerModal } from "./ColumnCustomizerModal";
import {
  ColumnCategory,
  PresetName,
  useColumnPreferences,
} from "./columnPreferences";
import { DiskStorageBadge } from "../../components/quicksettings/DiskStorageBadge";

interface TorrentToolbarProps {
  count: number;
  totalUploadSpeed: number;
  totalDownloadSpeed: number;
  seedingConfig: SeedingConfig | undefined;
  adjustSpeed: (
    field: "maxUploadSpeedKbps" | "maxDownloadSpeedKbps",
    factor: number,
  ) => void;
  filter: string;
  onFilterChange: (value: string) => void;
  viewMode: ViewMode;
  onViewModeChange: (mode: ViewMode) => void;
  onAddTorrent: () => void;
  onImportPackage?: () => void;
  onSearchIndexers?: () => void;
  onStartAll: () => void;
  onStopAll: () => void;
  selectedCount: number;
  bulkPending: boolean;
  onBulkStart: () => void;
  onBulkStop: () => void;
  onBulkDelete: () => void;
  onBulkClear: () => void;
  onBulkMoveQueue?: (position: "top" | "up" | "down" | "bottom") => void;
  onBulkAddTags?: () => void;
  onBulkRemoveTags?: () => void;
  isFilterCollapsed?: boolean;
  onToggleFilter?: () => void;
  isQuickControlsOpen?: boolean;
  onToggleQuickControls?: () => void;
  visibleColumns?: Set<string>;
  onToggleColumn?: (key: string) => void;
  onResetColumns?: () => void;
  onResetSort?: () => void;
  onSelectAllColumns?: () => void;
  onDeselectAllColumns?: () => void;
  onApplyColumnPreset?: (preset: PresetName) => void;
  onToggleCategoryColumns?: (
    category: ColumnCategory,
    enable?: boolean,
  ) => void;
  isColumnCustomizerOpen?: boolean;
  onToggleColumnCustomizer?: () => void;
}

export function TorrentToolbar({
  count,
  totalUploadSpeed: _totalUploadSpeed,
  totalDownloadSpeed: _totalDownloadSpeed,
  seedingConfig,
  adjustSpeed,
  filter,
  onFilterChange,
  viewMode,
  onViewModeChange,
  onAddTorrent,
  onImportPackage,
  onSearchIndexers,
  onStartAll,
  onStopAll,
  selectedCount,
  bulkPending,
  onBulkStart,
  onBulkStop,
  onBulkDelete,
  onBulkClear,
  onBulkMoveQueue,
  onBulkAddTags,
  onBulkRemoveTags,
  isFilterCollapsed = false,
  onToggleFilter,
  isQuickControlsOpen = false,
  onToggleQuickControls,
  visibleColumns,
  onToggleColumn,
  onResetColumns,
  onResetSort,
  onSelectAllColumns,
  onDeselectAllColumns,
  onApplyColumnPreset,
  onToggleCategoryColumns,
  isColumnCustomizerOpen,
  onToggleColumnCustomizer,
}: TorrentToolbarProps) {
  const { canMutateTorrents, canAddTorrent, canDeleteTorrent } =
    usePermissions();
  const [isInternalCustomizerOpen, setIsInternalCustomizerOpen] =
    useState(false);
  const defaultPrefs = useColumnPreferences();

  const isCustomizerOpen =
    isColumnCustomizerOpen !== undefined
      ? isColumnCustomizerOpen
      : isInternalCustomizerOpen;

  const handleOpenCustomizer = () => {
    if (onToggleColumnCustomizer) {
      onToggleColumnCustomizer();
    } else {
      setIsInternalCustomizerOpen(true);
    }
  };

  const handleCloseCustomizer = () => {
    if (onToggleColumnCustomizer) {
      onToggleColumnCustomizer();
    } else {
      setIsInternalCustomizerOpen(false);
    }
  };

  const activeVisibleColumns = visibleColumns ?? defaultPrefs.visibleColumns;
  const handleToggleColumn = onToggleColumn ?? defaultPrefs.toggleColumn;
  const handleResetDefaults = onResetColumns ?? defaultPrefs.resetToDefaults;
  const handleResetSort = onResetSort ?? defaultPrefs.resetSort;
  const handleSelectAll = onSelectAllColumns ?? defaultPrefs.selectAll;
  const handleDeselectAll = onDeselectAllColumns ?? defaultPrefs.deselectAll;
  const handleApplyPreset = onApplyColumnPreset ?? defaultPrefs.applyPreset;
  const handleToggleCategory =
    onToggleCategoryColumns ?? defaultPrefs.toggleCategory;
  const { t } = useTranslation();
  const [localFilter, setLocalFilter] = useState(filter);

  useEffect(() => {
    setLocalFilter(filter);
  }, [filter]);

  useEffect(() => {
    if (localFilter === filter) return;
    const timer = setTimeout(() => {
      onFilterChange(localFilter);
    }, 200);
    return () => clearTimeout(timer);
  }, [localFilter, filter, onFilterChange]);

  return (
    <div className="page-header">
      <div className="page-header-group">
        {onToggleFilter && (
          <button
            type="button"
            className={`btn btn-small toggle-filter-btn${isFilterCollapsed ? " active" : ""}`}
            onClick={onToggleFilter}
            title={
              isFilterCollapsed ? "Show filter sidebar" : "Hide filter sidebar"
            }
            aria-label="Toggle filter sidebar"
            style={{ display: "inline-flex", alignItems: "center", gap: "5px" }}
          >
            <FilterIcon size={12} />
            <span>
              {isFilterCollapsed
                ? `▶ ${t("torrents.toggleFilter", undefined, "Filter")}`
                : `◀ ${t("torrents.toggleFilter", undefined, "Filter")}`}
            </span>
          </button>
        )}
        <h1 className="page-heading">
          {t("torrents.title", undefined, "Torrents")} ({count})
        </h1>
        {selectedCount > 0 ? (
          <div className="bulk-actions">
            <span className="bulk-actions-count">
              {t(
                "torrents.selectedCount",
                { count: selectedCount },
                `${selectedCount} selected`,
              )}
            </span>
            <button
              className="btn btn-success"
              onClick={onBulkStart}
              disabled={bulkPending || !canMutateTorrents}
            >
              <PlayIcon size={13} /> {t("torrents.start", undefined, "Start")}
            </button>
            <button
              className="btn btn-outline"
              onClick={onBulkStop}
              disabled={bulkPending || !canMutateTorrents}
            >
              <StopIcon size={13} /> {t("torrents.stop", undefined, "Stop")}
            </button>
            {onBulkAddTags && (
              <button
                type="button"
                className="btn btn-outline bulk-add-tags-btn"
                onClick={onBulkAddTags}
                disabled={bulkPending || !canMutateTorrents}
                title={t("torrents.bulkAddTags", undefined, "Assign Tags")}
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "4px",
                }}
              >
                <TagIcon size={13} />{" "}
                {t("torrents.bulkAddTags", undefined, "Assign Tags")}
              </button>
            )}
            {onBulkRemoveTags && (
              <button
                type="button"
                className="btn btn-outline bulk-remove-tags-btn"
                onClick={onBulkRemoveTags}
                disabled={bulkPending || !canMutateTorrents}
                title={t("torrents.bulkRemoveTags", undefined, "Remove Tags")}
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "4px",
                }}
              >
                <TagIcon size={13} />{" "}
                {t("torrents.bulkRemoveTags", undefined, "Remove Tags")}
              </button>
            )}
            <button
              className="btn btn-danger"
              onClick={onBulkDelete}
              disabled={bulkPending || !canDeleteTorrent}
            >
              {t("common.delete", undefined, "Delete")}
            </button>
            {onBulkMoveQueue && (
              <div
                className="btn-group"
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "2px",
                  marginLeft: "4px",
                  borderLeft:
                    "1px solid var(--border, rgba(255, 255, 255, 0.15))",
                  paddingLeft: "6px",
                }}
              >
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("top")}
                  disabled={bulkPending || !canMutateTorrents}
                  title={t(
                    "torrents.contextMenu.top",
                    undefined,
                    "Move to Top",
                  )}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ⤒ {t("torrents.contextMenu.top", undefined, "Top")}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("up")}
                  disabled={bulkPending || !canMutateTorrents}
                  title={t("torrents.contextMenu.up", undefined, "Move Up")}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ▲ {t("torrents.contextMenu.up", undefined, "Up")}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("down")}
                  disabled={bulkPending || !canMutateTorrents}
                  title={t("torrents.contextMenu.down", undefined, "Move Down")}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ▼ {t("torrents.contextMenu.down", undefined, "Down")}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("bottom")}
                  disabled={bulkPending || !canMutateTorrents}
                  title={t(
                    "torrents.contextMenu.bottom",
                    undefined,
                    "Move to Bottom",
                  )}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ⤓ {t("torrents.contextMenu.bottom", undefined, "Bottom")}
                </button>
              </div>
            )}
            <button
              className="btn btn-outline"
              onClick={onBulkClear}
              disabled={bulkPending}
            >
              {t("common.clear", undefined, "Clear")}
            </button>
          </div>
        ) : (
          <>
            {canAddTorrent && (
              <button className="btn btn-success" onClick={onAddTorrent}>
                <PlusIcon size={13} />{" "}
                {t("torrents.addTorrent", undefined, "Add Torrent")}
              </button>
            )}
            {(canAddTorrent || canMutateTorrents) && onImportPackage && (
              <button
                type="button"
                className="btn btn-outline"
                onClick={onImportPackage}
                title={t("torrents.importPackage", undefined, "Import Package")}
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "5px",
                }}
              >
                <UploadIcon size={13} />{" "}
                <span>
                  {t("torrents.importPackage", undefined, "Import Package")}
                </span>
              </button>
            )}
            {onSearchIndexers && (
              <button
                type="button"
                className="btn btn-outline"
                onClick={onSearchIndexers}
                style={{ fontSize: "0.82rem" }}
              >
                🔍 {t("modals.indexerSearch", undefined, "Search Indexers")}
              </button>
            )}
            {onToggleQuickControls && (
              <button
                type="button"
                className={`btn btn-small quick-controls-toggle-btn${isQuickControlsOpen ? " active" : ""}`}
                onClick={onToggleQuickControls}
                title={
                  isQuickControlsOpen
                    ? "Hide Quick Controls (Q)"
                    : "Show Quick Controls (Q)"
                }
                aria-label="Toggle Quick Controls drawer"
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                }}
              >
                <SlidersIcon size={13} />
                <span className="quick-controls-label">
                  <span className="quick-controls-dot" />
                  {t("torrents.quickControls", undefined, "Quick Controls")}
                </span>
                <kbd className="quick-controls-kbd">Q</kbd>
              </button>
            )}
            <DiskStorageBadge compact />
          </>
        )}
      </div>
      <div className="page-header-actions">
        <button
          className="btn btn-success"
          onClick={onStartAll}
          disabled={!canMutateTorrents}
        >
          <PlayIcon size={13} />{" "}
          {t("torrents.startAll", undefined, "Start All")}
        </button>
        <button
          className="btn btn-danger"
          onClick={onStopAll}
          disabled={!canMutateTorrents}
        >
          <StopIcon size={13} /> {t("torrents.stopAll", undefined, "Stop All")}
        </button>
        <div
          className="speed-controls"
          style={{ display: "flex", alignItems: "center", gap: "4px" }}
        >
          <span style={{ fontSize: "0.85em", opacity: 0.8, fontWeight: 600 }}>
            {t("torrents.uploadLimit", undefined, "UL Limit:")}
          </span>
          <button
            className="btn btn-small btn-success"
            onClick={() => adjustSpeed("maxUploadSpeedKbps", 2)}
            title="Double upload speed limit"
            aria-label="Double upload speed limit"
            disabled={!seedingConfig || !canMutateTorrents}
          >
            &#9650;&#9650;
          </button>
          <button
            className="btn btn-small btn-success"
            onClick={() => adjustSpeed("maxUploadSpeedKbps", 0.5)}
            title="Halve upload speed limit"
            aria-label="Halve upload speed limit"
            disabled={!seedingConfig || !canMutateTorrents}
          >
            &#9660;&#9660;
          </button>
          <span
            style={{
              fontSize: "0.85em",
              opacity: 0.8,
              marginLeft: "8px",
              fontWeight: 600,
            }}
          >
            {t("torrents.downloadLimit", undefined, "DL Limit:")}
          </span>
          <button
            className="btn btn-small btn-danger"
            onClick={() => adjustSpeed("maxDownloadSpeedKbps", 2)}
            title="Double download speed limit"
            aria-label="Double download speed limit"
            disabled={!seedingConfig || !canMutateTorrents}
          >
            &#9650;&#9650;
          </button>
          <button
            className="btn btn-small btn-danger"
            onClick={() => adjustSpeed("maxDownloadSpeedKbps", 0.5)}
            title="Halve download speed limit"
            aria-label="Halve download speed limit"
            disabled={!seedingConfig || !canMutateTorrents}
          >
            &#9660;&#9660;
          </button>
        </div>
        <input
          type="text"
          className="search-input"
          placeholder={t(
            "torrents.filterPlaceholder",
            undefined,
            "Filter torrents...",
          )}
          aria-label={t(
            "torrents.filterPlaceholder",
            undefined,
            "Filter torrents",
          )}
          value={localFilter}
          onChange={(e) => setLocalFilter(e.target.value)}
        />
        <div className="view-toggle">
          <button
            className={`view-toggle-btn${viewMode === "table" ? " active" : ""}`}
            onClick={() => onViewModeChange("table")}
            title="Table view"
            aria-pressed={viewMode === "table"}
          >
            <TableIcon size={13} />{" "}
            {t("torrents.tableView", undefined, "Table")}
          </button>
          <button
            className={`view-toggle-btn${viewMode === "grid" ? " active" : ""}`}
            onClick={() => onViewModeChange("grid")}
            title="Grid view"
            aria-pressed={viewMode === "grid"}
          >
            <GridIcon size={13} /> {t("torrents.gridView", undefined, "Grid")}
          </button>
        </div>
        {viewMode === "table" && (
          <button
            type="button"
            className={`btn btn-outline column-customizer-btn${isCustomizerOpen ? " active" : ""}`}
            onClick={handleOpenCustomizer}
            title={t("torrents.columns", undefined, "Columns")}
            aria-label={t("torrents.columns", undefined, "Columns")}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "6px",
              fontSize: "0.82rem",
            }}
          >
            <ColumnsIcon size={13} />
            <span>{t("torrents.columns", undefined, "Columns")}</span>
          </button>
        )}
      </div>
      <ColumnCustomizerModal
        isOpen={isCustomizerOpen}
        onClose={handleCloseCustomizer}
        visibleColumns={activeVisibleColumns}
        onToggleColumn={handleToggleColumn}
        onResetToDefaults={handleResetDefaults}
        onResetSort={handleResetSort}
        onSelectAll={handleSelectAll}
        onDeselectAll={handleDeselectAll}
        onApplyPreset={handleApplyPreset}
        onToggleCategory={handleToggleCategory}
      />
    </div>
  );
}
