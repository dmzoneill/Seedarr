import { useState } from 'react';
import TorrentTable from '../components/TorrentTable';
import TorrentGrid from '../components/TorrentGrid';
import AddTorrentModal from '../components/AddTorrentModal';
import {
  useTorrents,
  useStartAllSeeding,
  useStopAllSeeding,
} from '../api/hooks';

type ViewMode = 'table' | 'grid';

function getInitialViewMode(): ViewMode {
  const stored = localStorage.getItem('seedarr-view-mode');
  return stored === 'grid' ? 'grid' : 'table';
}

function TorrentIndex() {
  const { data: torrents } = useTorrents();
  const startAll = useStartAllSeeding();
  const stopAll = useStopAllSeeding();
  const [filter, setFilter] = useState('');
  const [showAddModal, setShowAddModal] = useState(false);
  const [viewMode, setViewMode] = useState<ViewMode>(getInitialViewMode);

  const count = torrents?.length ?? 0;

  function handleViewMode(mode: ViewMode) {
    setViewMode(mode);
    localStorage.setItem('seedarr-view-mode', mode);
  }

  return (
    <div>
      <div className="page-header">
        <h1 className="page-heading">Torrents ({count})</h1>
        <div className="page-header-actions">
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
              Table
            </button>
            <button
              className={`view-toggle-btn${viewMode === 'grid' ? ' active' : ''}`}
              onClick={() => handleViewMode('grid')}
              title="Grid view"
            >
              Grid
            </button>
          </div>
          <button
            className="btn btn-success"
            onClick={() => setShowAddModal(true)}
          >
            Add Torrent
          </button>
          <button
            className="btn btn-success"
            onClick={() => startAll.mutate()}
          >
            Start All
          </button>
          <button className="btn" onClick={() => stopAll.mutate()}>
            Stop All
          </button>
        </div>
      </div>
      {viewMode === 'table' ? (
        <TorrentTable filter={filter} />
      ) : (
        <TorrentGrid filter={filter} />
      )}
      {showAddModal && (
        <AddTorrentModal onClose={() => setShowAddModal(false)} />
      )}
    </div>
  );
}

export default TorrentIndex;
