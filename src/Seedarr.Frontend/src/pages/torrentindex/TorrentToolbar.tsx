import { SeedingConfig } from '../../api/types';
import { formatSpeed } from '../../utils/formatters';
import {
  PlusIcon, PlayIcon, StopIcon, TableIcon, GridIcon,
} from '../../components/icons/UIIcons';
import { ViewMode } from './types';

interface TorrentToolbarProps {
  count: number;
  totalUploadSpeed: number;
  totalDownloadSpeed: number;
  seedingConfig: SeedingConfig | undefined;
  adjustSpeed: (field: 'maxUploadSpeedKbps' | 'maxDownloadSpeedKbps', factor: number) => void;
  filter: string;
  onFilterChange: (value: string) => void;
  viewMode: ViewMode;
  onViewModeChange: (mode: ViewMode) => void;
  onAddTorrent: () => void;
  onStartAll: () => void;
  onStopAll: () => void;
  selectedCount: number;
  bulkPending: boolean;
  onBulkStart: () => void;
  onBulkStop: () => void;
  onBulkDelete: () => void;
  onBulkClear: () => void;
}

export function TorrentToolbar({
  count,
  totalUploadSpeed,
  totalDownloadSpeed,
  seedingConfig,
  adjustSpeed,
  filter,
  onFilterChange,
  viewMode,
  onViewModeChange,
  onAddTorrent,
  onStartAll,
  onStopAll,
  selectedCount,
  bulkPending,
  onBulkStart,
  onBulkStop,
  onBulkDelete,
  onBulkClear,
}: TorrentToolbarProps) {
  return (
    <div className="page-header">
      <div className="page-header-group">
        <h1 className="page-heading">Torrents ({count})</h1>
        <button className="btn btn-success" onClick={onAddTorrent}>
          <PlusIcon size={13} /> Add Torrent
        </button>
        {selectedCount > 0 && (
          <div className="bulk-actions">
            <span className="bulk-actions-count">{selectedCount} selected</span>
            <button className="btn btn-small btn-success" onClick={onBulkStart} disabled={bulkPending}>
              <PlayIcon size={12} /> Start
            </button>
            <button className="btn btn-small" onClick={onBulkStop} disabled={bulkPending}>
              <StopIcon size={12} /> Stop
            </button>
            <button className="btn btn-small btn-danger" onClick={onBulkDelete} disabled={bulkPending}>
              Delete
            </button>
            <button className="btn btn-small" onClick={onBulkClear} disabled={bulkPending}>
              Clear
            </button>
          </div>
        )}
      </div>
      <div className="page-header-actions">
        <button className="btn btn-success" onClick={onStartAll}>
          <PlayIcon size={13} /> Start All
        </button>
        <button className="btn btn-danger" onClick={onStopAll}>
          <StopIcon size={13} /> Stop All
        </button>
        <div className="speed-controls" style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
          <span style={{ fontSize: '0.85em', opacity: 0.8 }}>
            UL: {formatSpeed(totalUploadSpeed)}
          </span>
          <button
            className="btn btn-small btn-success"
            onClick={() => adjustSpeed('maxUploadSpeedKbps', 2)}
            title="Double upload speed limit"
            disabled={!seedingConfig}
          >
            &#9650;&#9650;
          </button>
          <button
            className="btn btn-small btn-success"
            onClick={() => adjustSpeed('maxUploadSpeedKbps', 0.5)}
            title="Halve upload speed limit"
            disabled={!seedingConfig}
          >
            &#9660;&#9660;
          </button>
          <span style={{ fontSize: '0.85em', opacity: 0.8, marginLeft: '8px' }}>
            DL: {formatSpeed(totalDownloadSpeed)}
          </span>
          <button
            className="btn btn-small btn-danger"
            onClick={() => adjustSpeed('maxDownloadSpeedKbps', 2)}
            title="Double download speed limit"
            disabled={!seedingConfig}
          >
            &#9650;&#9650;
          </button>
          <button
            className="btn btn-small btn-danger"
            onClick={() => adjustSpeed('maxDownloadSpeedKbps', 0.5)}
            title="Halve download speed limit"
            disabled={!seedingConfig}
          >
            &#9660;&#9660;
          </button>
        </div>
        <input
          type="text"
          className="search-input"
          placeholder="Filter torrents..."
          value={filter}
          onChange={(e) => onFilterChange(e.target.value)}
        />
        <div className="view-toggle">
          <button
            className={`view-toggle-btn${viewMode === 'table' ? ' active' : ''}`}
            onClick={() => onViewModeChange('table')}
            title="Table view"
          >
            <TableIcon size={13} /> Table
          </button>
          <button
            className={`view-toggle-btn${viewMode === 'grid' ? ' active' : ''}`}
            onClick={() => onViewModeChange('grid')}
            title="Grid view"
          >
            <GridIcon size={13} /> Grid
          </button>
        </div>
      </div>
    </div>
  );
}
