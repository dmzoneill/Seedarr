import { useState } from 'react';
import { useParams, Link } from 'react-router';
import { useTorrent, useStartSeeding, useStopSeeding } from '../api/hooks';
import PeerList from '../components/PeerList';
import { GeneralTab } from './torrentdetails/GeneralTab';
import { FilesTab } from './torrentdetails/FilesTab';
import { TrackersTab } from './torrentdetails/TrackersTab';
import { OptionsTab } from './torrentdetails/OptionsTab';
import { MonitoringTab } from './torrentdetails/MonitoringTab';
import { LogTab } from './torrentdetails/LogTab';
import { TorrentDetailSkeleton } from './torrentdetails/shared';

type Tab = 'general' | 'files' | 'trackers' | 'options' | 'peers' | 'monitoring' | 'log';

const tabs: { key: Tab; label: string }[] = [
  { key: 'general', label: 'General' },
  { key: 'files', label: 'Files' },
  { key: 'trackers', label: 'Trackers' },
  { key: 'options', label: 'Options' },
  { key: 'peers', label: 'Peers' },
  { key: 'monitoring', label: 'Monitoring' },
  { key: 'log', label: 'Log' },
];

function TorrentDetails() {
  const { id } = useParams<{ id: string }>();
  const parsed = Number(id);
  const isValidId = id !== undefined && !isNaN(parsed) && parsed > 0;
  const torrentId = isValidId ? parsed : 0;
  const { data: torrent, isLoading, error } = useTorrent(torrentId);
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const [activeTab, setActiveTab] = useState<Tab>('general');

  if (!isValidId) {
    return (
      <div>
        <Link to="/torrents" className="back-link">Back to Torrents</Link>
        <p className="error">Invalid torrent ID.</p>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div>
        <Link to="/torrents" className="back-link">Back to Torrents</Link>
        <TorrentDetailSkeleton />
      </div>
    );
  }

  if (error || !torrent) {
    return (
      <div>
        <Link to="/torrents" className="back-link">Back to Torrents</Link>
        <p className="error">Torrent not found.</p>
      </div>
    );
  }

  const isSeeding = torrent.status === 'Seeding';

  return (
    <div>
      <Link to="/torrents" className="back-link">Back to Torrents</Link>
      <h1 className="page-heading">{torrent.name}</h1>

      <div className="torrent-detail-actions">
        {isSeeding ? (
          <button className="btn btn-danger" onClick={() => stopSeeding.mutate(torrent.id)}>
            Stop Seeding
          </button>
        ) : (
          <button className="btn btn-success" onClick={() => startSeeding.mutate(torrent.id)}>
            Start Seeding
          </button>
        )}
      </div>

      <nav className="tab-nav">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            className={`tab-btn${activeTab === tab.key ? ' tab-btn-active' : ''}`}
            onClick={() => setActiveTab(tab.key)}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      {activeTab === 'general' && <GeneralTab torrent={torrent} />}
      {activeTab === 'files' && <FilesTab torrent={torrent} />}
      {activeTab === 'trackers' && <TrackersTab torrent={torrent} />}
      {activeTab === 'options' && <OptionsTab torrent={torrent} />}
      {activeTab === 'peers' && <PeerList torrentId={torrent.id} />}
      {activeTab === 'monitoring' && <MonitoringTab torrent={torrent} />}
      {activeTab === 'log' && <LogTab torrent={torrent} />}
    </div>
  );
}

export default TorrentDetails;
