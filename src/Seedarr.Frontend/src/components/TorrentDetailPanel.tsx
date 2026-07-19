import { useState } from 'react';
import { useTorrent, useStartSeeding, useStopSeeding } from '../api/hooks';
import {
  InfoIcon, ClipboardIcon, FileIcon, UsersIcon, GlobeIcon,
  SlidersIcon, ActivityIcon, HashIcon,
} from './icons/UIIcons';
import { usePanelHeight } from './torrentdetailpanel/shared';
import { StatusTab } from './torrentdetailpanel/StatusTab';
import { DetailsTab } from './torrentdetailpanel/DetailsTab';
import { FilesTab } from './torrentdetailpanel/FilesTab';
import { PeersTab } from './torrentdetailpanel/PeersTab';
import { TrackersTab } from './torrentdetailpanel/TrackersTab';
import { OptionsTab } from './torrentdetailpanel/OptionsTab';
import { MonitoringTab } from './torrentdetailpanel/MonitoringTab';
import { LogTab } from './torrentdetailpanel/LogTab';

type DetailTab = 'status' | 'details' | 'files' | 'peers' | 'trackers' | 'options' | 'monitoring' | 'log';

interface TorrentDetailPanelProps {
  torrentId: number;
  onClose: () => void;
}

const TAB_ICONS: Record<DetailTab, React.ReactNode> = {
  status: <InfoIcon size={13} />,
  details: <ClipboardIcon size={13} />,
  files: <FileIcon size={13} />,
  peers: <UsersIcon size={13} />,
  trackers: <GlobeIcon size={13} />,
  options: <SlidersIcon size={13} />,
  monitoring: <ActivityIcon size={13} />,
  log: <HashIcon size={13} />,
};

const DETAIL_TABS: { key: DetailTab; label: string }[] = [
  { key: 'status', label: 'Status' },
  { key: 'details', label: 'Details' },
  { key: 'files', label: 'Files' },
  { key: 'peers', label: 'Peers' },
  { key: 'trackers', label: 'Trackers' },
  { key: 'options', label: 'Options' },
  { key: 'monitoring', label: 'Monitoring' },
  { key: 'log', label: 'Log' },
];

function TorrentDetailPanel({ torrentId, onClose }: TorrentDetailPanelProps) {
  const { data: torrent, isLoading, isError } = useTorrent(torrentId);
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const [tab, setTab] = useState<DetailTab>('status');
  const { height, panelRef, onMouseDown } = usePanelHeight();

  if (isLoading) return <div className="detail-panel" style={{ height }}><div className="detail-panel-loading">Loading...</div></div>;
  if (isError) return <div className="detail-panel" style={{ height }}><div className="detail-panel-empty">Failed to load torrent.</div></div>;
  if (!torrent) return <div className="detail-panel" style={{ height }}><div className="detail-panel-empty">Torrent not found</div></div>;

  const isSeeding = torrent.status === 'Seeding';

  return (
    <div className="detail-panel" ref={panelRef} style={{ height }}>
      <div className="detail-panel-resize-handle" onMouseDown={onMouseDown} />
      <div className="detail-panel-header">
        <div className="detail-panel-title">{torrent.name}</div>
        <div className="detail-panel-actions">
          {isSeeding ? (
            <button className="btn btn-small btn-danger" onClick={() => stopSeeding.mutate(torrent.id)}>Stop</button>
          ) : (
            <button className="btn btn-small btn-success" onClick={() => startSeeding.mutate(torrent.id)}>Start</button>
          )}
          <button className="btn btn-small" onClick={onClose} title="Close panel">X</button>
        </div>
      </div>
      <nav className="detail-panel-tabs">
        {DETAIL_TABS.map((t) => (
          <button
            key={t.key}
            className={`tab-btn${tab === t.key ? ' tab-btn-active' : ''}`}
            onClick={() => setTab(t.key)}
          >
            {TAB_ICONS[t.key]} {t.label}
          </button>
        ))}
      </nav>
      <div className="detail-panel-body">
        {tab === 'status' && <StatusTab torrent={torrent} />}
        {tab === 'details' && <DetailsTab torrent={torrent} />}
        {tab === 'files' && <FilesTab torrentId={torrent.id} />}
        {tab === 'peers' && <PeersTab torrentId={torrent.id} />}
        {tab === 'trackers' && <TrackersTab torrentId={torrent.id} />}
        {tab === 'options' && <OptionsTab torrent={torrent} />}
        {tab === 'monitoring' && <MonitoringTab torrent={torrent} />}
        {tab === 'log' && <LogTab torrent={torrent} />}
      </div>
    </div>
  );
}

export default TorrentDetailPanel;
