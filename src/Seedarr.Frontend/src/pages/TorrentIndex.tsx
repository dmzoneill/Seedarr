import { useState } from 'react';
import TorrentTable from '../components/TorrentTable';
import {
  useTorrents,
  useStartAllSeeding,
  useStopAllSeeding,
} from '../api/hooks';

function TorrentIndex() {
  const { data: torrents } = useTorrents();
  const startAll = useStartAllSeeding();
  const stopAll = useStopAllSeeding();
  const [filter, setFilter] = useState('');

  const count = torrents?.length ?? 0;

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
      <TorrentTable filter={filter} />
    </div>
  );
}

export default TorrentIndex;
