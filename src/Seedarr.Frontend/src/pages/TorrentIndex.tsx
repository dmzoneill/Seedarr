import { useState, useMemo, useCallback, useEffect } from 'react';
import { useSearchParams } from 'react-router';
import TorrentTable from '../components/TorrentTable';
import TorrentGrid from '../components/TorrentGrid';
import TorrentDetailPanel from '../components/TorrentDetailPanel';
import AddTorrentModal from '../components/AddTorrentModal';
import {
  useTorrents,
  useStartSeeding,
  useStopSeeding,
  useDeleteTorrent,
  useStartAllSeeding,
  useStopAllSeeding,
  useSeedingConfig,
  useSaveSeedingConfig,
} from '../api/hooks';
import { extractTrackerDomain, formatSpeed } from '../utils/formatters';
import {
  AllIcon, SeedingIcon, StoppedIcon, QueuedIcon, ErrorIcon,
  PlusIcon, PlayIcon, StopIcon, TableIcon, GridIcon, GlobeIcon,
} from '../components/icons/UIIcons';

type ViewMode = 'table' | 'grid';

const STATE_FILTER_ICONS: Record<string, React.ReactNode> = {
  All: <AllIcon size={13} />,
  Seeding: <SeedingIcon size={13} />,
  Stopped: <StoppedIcon size={13} />,
  Queued: <QueuedIcon size={13} />,
  Error: <ErrorIcon size={13} />,
};

const STATE_FILTERS = ['All', 'Seeding', 'Stopped', 'Queued', 'Error'] as const;

function getInitialViewMode(): ViewMode {
  const stored = localStorage.getItem('seedarr-view-mode');
  return stored === 'grid' ? 'grid' : 'table';
}

function TorrentIndex() {
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: torrents } = useTorrents();
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const deleteTorrent = useDeleteTorrent();
  const startAll = useStartAllSeeding();
  const stopAll = useStopAllSeeding();
  const { data: seedingConfig } = useSeedingConfig();
  const saveSeedingConfig = useSaveSeedingConfig();
  const [filter, setFilter] = useState(() => searchParams.get('q') || '');
  const [showAddModal, setShowAddModal] = useState(false);
  const [selectMode, setSelectMode] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());

  // Sync filter from URL search params when they change
  useEffect(() => {
    const q = searchParams.get('q');
    if (q) {
      setFilter(q);
      // Clear the param after consuming it so the URL stays clean
      setSearchParams({}, { replace: true });
    }
  }, [searchParams, setSearchParams]);
  const [viewMode, setViewMode] = useState<ViewMode>(getInitialViewMode);
  const [selectedState, setSelectedState] = useState<string>('All');
  const [selectedTracker, setSelectedTracker] = useState<string>('All');
  const [selectedTorrentId, setSelectedTorrentId] = useState<number | null>(null);

  const count = torrents?.length ?? 0;

  const adjustSpeed = useCallback(
    (field: 'maxUploadSpeedKbps' | 'maxDownloadSpeedKbps', factor: number) => {
      if (!seedingConfig) return;
      const current = seedingConfig[field];
      const newSpeed = Math.max(1, Math.round(current * factor));
      saveSeedingConfig.mutate({ ...seedingConfig, [field]: newSpeed });
    },
    [seedingConfig, saveSeedingConfig]
  );

  const stateCounts = useMemo(() => {
    const all = torrents ?? [];
    const counts: Record<string, number> = {
      All: all.length,
      Seeding: 0,
      Stopped: 0,
      Queued: 0,
      Error: 0,
    };
    for (const t of all) {
      if (t.status in counts) {
        counts[t.status]++;
      }
    }
    return counts;
  }, [torrents]);

  const trackerGroups = useMemo(() => {
    const all = torrents ?? [];
    const groups: Record<string, number> = {};
    for (const t of all) {
      const domain = extractTrackerDomain(t.trackerUrl);
      groups[domain] = (groups[domain] || 0) + 1;
    }
    return Object.entries(groups).sort((a, b) => a[0].localeCompare(b[0]));
  }, [torrents]);

  const { totalUploadSpeed, totalDownloadSpeed } = useMemo(() => {
    const all = torrents ?? [];
    let ul = 0;
    let dl = 0;
    for (const t of all) {
      ul += t.uploadSpeed ?? 0;
      dl += t.downloadSpeed ?? 0;
    }
    return { totalUploadSpeed: ul, totalDownloadSpeed: dl };
  }, [torrents]);

  function handleViewMode(mode: ViewMode) {
    setViewMode(mode);
    localStorage.setItem('seedarr-view-mode', mode);
  }

  return (
    <div className="torrent-index-page">
      <div className="page-header">
        <h1 className="page-heading">Torrents ({count})</h1>
        <div className="page-header-actions">
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
            onChange={(e) => setFilter(e.target.value)}
          />
          <div className="view-toggle">
            <button
              className={`view-toggle-btn${viewMode === 'table' ? ' active' : ''}`}
              onClick={() => handleViewMode('table')}
              title="Table view"
            >
              <TableIcon size={13} /> Table
            </button>
            <button
              className={`view-toggle-btn${viewMode === 'grid' ? ' active' : ''}`}
              onClick={() => handleViewMode('grid')}
              title="Grid view"
            >
              <GridIcon size={13} /> Grid
            </button>
          </div>
          <button
            className="btn btn-success"
            onClick={() => setShowAddModal(true)}
          >
            <PlusIcon size={13} /> Add Torrent
          </button>
          <button
            className="btn btn-success"
            onClick={() => startAll.mutate()}
          >
            <PlayIcon size={13} /> Start All
          </button>
          <button className="btn btn-danger" onClick={() => stopAll.mutate()}>
            <StopIcon size={13} /> Stop All
          </button>
          <button
            className={`btn ${selectMode ? 'btn-primary' : 'btn-default'}`}
            onClick={() => { setSelectMode(!selectMode); setSelectedIds(new Set()); }}
          >
            Select
          </button>
        </div>
      </div>
      <div className="torrent-content-layout">
        <div className="filter-panel">
          <div className="filter-panel-section">State</div>
          <ul className="filter-panel-list">
            {STATE_FILTERS.map((state) => (
              <li key={state}>
                <button
                  className={`filter-panel-item${selectedState === state ? ' active' : ''}`}
                  onClick={() => setSelectedState(state)}
                >
                  <span className="filter-panel-label">{STATE_FILTER_ICONS[state]} {state}</span>
                  <span className="filter-panel-count">{stateCounts[state] ?? 0}</span>
                </button>
              </li>
            ))}
          </ul>
          <div className="filter-panel-section">Tracker</div>
          <ul className="filter-panel-list">
            <li>
              <button
                className={`filter-panel-item${selectedTracker === 'All' ? ' active' : ''}`}
                onClick={() => setSelectedTracker('All')}
              >
                <span className="filter-panel-label"><AllIcon size={13} /> All</span>
                <span className="filter-panel-count">{count}</span>
              </button>
            </li>
            {trackerGroups.map(([domain, groupCount]) => (
              <li key={domain}>
                <button
                  className={`filter-panel-item${selectedTracker === domain ? ' active' : ''}`}
                  onClick={() => setSelectedTracker(domain)}
                >
                  <span className="filter-panel-label"><GlobeIcon size={13} /> {domain}</span>
                  <span className="filter-panel-count">{groupCount}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
        <div className="filter-content">
          <div className="torrent-split-pane">
            <div className="torrent-split-top">
              {viewMode === 'table' ? (
                <TorrentTable
                  filter={filter}
                  stateFilter={selectedState}
                  trackerFilter={selectedTracker}
                  selectedTorrentId={selectedTorrentId}
                  onSelectTorrent={setSelectedTorrentId}
                  selectMode={selectMode}
                  selectedIds={selectedIds}
                  onToggleSelect={(id) => {
                    setSelectedIds((prev) => {
                      const next = new Set(prev);
                      if (next.has(id)) next.delete(id);
                      else next.add(id);
                      return next;
                    });
                  }}
                  onSelectAll={(ids) => {
                    setSelectedIds((prev) =>
                      prev.size === ids.length ? new Set() : new Set(ids)
                    );
                  }}
                />
              ) : (
                <TorrentGrid filter={filter} stateFilter={selectedState} trackerFilter={selectedTracker} />
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
      {selectMode && selectedIds.size > 0 && (
        <div className="card" style={{ position: 'sticky', bottom: 0, zIndex: 10, display: 'flex', alignItems: 'center', gap: 8, padding: '8px 16px', margin: '8px 0 0' }}>
          <span style={{ fontWeight: 600 }}>{selectedIds.size} selected</span>
          <button className="btn btn-success btn-sm" onClick={() => { selectedIds.forEach((id) => startSeeding.mutate(id)); setSelectedIds(new Set()); }}>
            <PlayIcon size={12} /> Start
          </button>
          <button className="btn btn-danger btn-sm" onClick={() => { selectedIds.forEach((id) => stopSeeding.mutate(id)); setSelectedIds(new Set()); }}>
            <StopIcon size={12} /> Stop
          </button>
          <button className="btn btn-danger btn-sm" onClick={() => {
            if (confirm(`Delete ${selectedIds.size} torrent(s)?`)) {
              selectedIds.forEach((id) => deleteTorrent.mutate({ id }));
              setSelectedIds(new Set());
            }
          }}>
            Delete
          </button>
          <button className="btn btn-default btn-sm" onClick={() => setSelectedIds(new Set())}>
            Clear
          </button>
        </div>
      )}
      {showAddModal && (
        <AddTorrentModal onClose={() => setShowAddModal(false)} />
      )}
    </div>
  );
}

export default TorrentIndex;
