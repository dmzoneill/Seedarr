import { useState, useEffect, useRef, useCallback } from 'react';
import {
  useTorrent,
  useTorrentFiles,
  useTorrentTrackers,
  usePeers,
  useUpdateTorrent,
  useStartSeeding,
  useStopSeeding,
  useTorrentSpeedHistory,
} from '../api/hooks';
import { formatBytes, formatSpeed, formatRatio, formatDate } from '../utils/formatters';
import type { Torrent, TorrentFileInfo, TrackerEntry, Peer } from '../api/types';
import {
  InfoIcon, ClipboardIcon, FileIcon, UsersIcon, GlobeIcon,
  SlidersIcon, ActivityIcon, HashIcon,
} from './icons/UIIcons';

type DetailTab = 'status' | 'details' | 'files' | 'peers' | 'trackers' | 'options' | 'monitoring' | 'log';

interface TorrentDetailPanelProps {
  torrentId: number;
  onClose: () => void;
}

function StatusTab({ torrent }: { torrent: Torrent }) {
  const rows: [string, string][] = [
    ['Status', torrent.status],
    ['Progress', `${(torrent.progress * 100).toFixed(1)}%`],
    ['Uploaded', formatBytes(torrent.uploaded)],
    ['Downloaded', formatBytes(torrent.downloaded)],
    ['Ratio', formatRatio(torrent.ratio)],
    ['Seeders', String(torrent.seeders)],
    ['Leechers', String(torrent.leechers)],
    ['Upload Limit', torrent.uploadLimit > 0 ? `${torrent.uploadLimit} KB/s` : 'Unlimited'],
    ['Download Limit', torrent.downloadLimit > 0 ? `${torrent.downloadLimit} KB/s` : 'Unlimited'],
    ['Priority', torrent.priority === 2 ? 'High' : torrent.priority === 1 ? 'Normal' : 'Low'],
    ['Super Seeding', torrent.superSeeding ? 'Yes' : 'No'],
    ['Force Start', torrent.forceStart ? 'Yes' : 'No'],
    ['Label', torrent.label ?? '-'],
    ['Added', formatDate(torrent.dateAdded)],
    ['Last Active', formatDate(torrent.lastActive)],
  ];

  return (
    <div className="detail-panel-grid">
      {rows.map(([label, value]) => (
        <div key={label} className="detail-panel-row">
          <span className="detail-panel-label">{label}</span>
          <span className="detail-panel-value">{value}</span>
        </div>
      ))}
    </div>
  );
}

function DetailsTab({ torrent }: { torrent: Torrent }) {
  const rows: [string, string][] = [
    ['Name', torrent.name],
    ['Info Hash', torrent.infoHash],
    ['Total Size', formatBytes(torrent.totalSize)],
    ['Pieces', `${torrent.pieceCount} x ${formatBytes(torrent.pieceLength)}`],
    ['Private', torrent.isPrivate ? 'Yes' : 'No'],
    ['Tracker', torrent.trackerUrl ?? '-'],
  ];
  if (torrent.creationDate) rows.push(['Created', formatDate(torrent.creationDate)]);
  if (torrent.createdBy) rows.push(['Created By', torrent.createdBy]);
  if (torrent.comment) rows.push(['Comment', torrent.comment]);
  if (torrent.sourcePath) rows.push(['Source Path', torrent.sourcePath]);

  return (
    <div className="detail-panel-grid">
      {rows.map(([label, value]) => (
        <div key={label} className="detail-panel-row">
          <span className="detail-panel-label">{label}</span>
          <span className="detail-panel-value mono">{value}</span>
        </div>
      ))}
    </div>
  );
}

function FilesTab({ torrentId }: { torrentId: number }) {
  const { data: files, isLoading } = useTorrentFiles(torrentId);

  if (isLoading) return <div className="detail-panel-loading">Loading files...</div>;
  if (!files || files.length === 0) return <div className="detail-panel-empty">No files</div>;

  return (
    <div className="detail-panel-table-wrap">
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th">Path</th>
            <th className="torrent-table-th">Size</th>
          </tr>
        </thead>
        <tbody>
          {files.map((f) => (
            <tr key={f.id} className="torrent-table-row">
              <td className="mono">{f.path}</td>
              <td>{formatBytes(f.size)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PeersTab({ torrentId }: { torrentId: number }) {
  const { data: peers, isLoading } = usePeers(torrentId);

  if (isLoading) return <div className="detail-panel-loading">Loading peers...</div>;
  if (!peers || peers.length === 0) return <div className="detail-panel-empty">No peers connected</div>;

  return (
    <div className="detail-panel-table-wrap">
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th">Address</th>
            <th className="torrent-table-th">Client</th>
            <th className="torrent-table-th">Progress</th>
            <th className="torrent-table-th">Up Speed</th>
            <th className="torrent-table-th">Down Speed</th>
            <th className="torrent-table-th">Uploaded</th>
            <th className="torrent-table-th">Downloaded</th>
            <th className="torrent-table-th">Flags</th>
          </tr>
        </thead>
        <tbody>
          {peers.map((p) => (
            <tr key={p.id} className="torrent-table-row">
              <td className="mono">{p.ip}:{p.port}</td>
              <td>{p.client}</td>
              <td>{(p.progress * 100).toFixed(1)}%</td>
              <td>{formatSpeed(p.uploadSpeed)}</td>
              <td>{formatSpeed(p.downloadSpeed)}</td>
              <td>{formatBytes(p.uploaded)}</td>
              <td>{formatBytes(p.downloaded)}</td>
              <td className="mono">{p.flags}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function trackerBadgeClass(status: string): string {
  switch (status) {
    case 'Working': return 'badge-seeding';
    case 'Announcing': return 'badge-announcing';
    case 'Failed': return 'badge-error';
    case 'Disabled': return 'badge-stopped';
    default: return 'badge-warning';
  }
}

function TrackersTab({ torrentId }: { torrentId: number }) {
  const { data: trackers, isLoading } = useTorrentTrackers(torrentId);

  if (isLoading) return <div className="detail-panel-loading">Loading trackers...</div>;
  if (!trackers || trackers.length === 0) return <div className="detail-panel-empty">No trackers</div>;

  return (
    <div className="detail-panel-table-wrap">
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th">URL</th>
            <th className="torrent-table-th">Tier</th>
            <th className="torrent-table-th">Status</th>
            <th className="torrent-table-th">Seeders</th>
            <th className="torrent-table-th">Leechers</th>
            <th className="torrent-table-th">Announces</th>
            <th className="torrent-table-th">Last Announce</th>
          </tr>
        </thead>
        <tbody>
          {trackers.map((t) => (
            <tr key={t.id} className="torrent-table-row">
              <td className="mono">{t.url}</td>
              <td>{t.tier}</td>
              <td><span className={`badge ${trackerBadgeClass(t.status)}`}>{t.status}</span></td>
              <td>{t.seeders}</td>
              <td>{t.leechers}</td>
              <td>{t.successfulAnnounces}/{t.totalAnnounces}</td>
              <td>{formatDate(t.lastAnnounce)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

const PRIORITY_OPTIONS = [
  { value: '0', label: 'Low' },
  { value: '1', label: 'Normal' },
  { value: '2', label: 'High' },
];

function OptionsTab({ torrent }: { torrent: Torrent }) {
  const updateTorrent = useUpdateTorrent();
  const [priority, setPriority] = useState(String(torrent.priority));
  const [uploadLimit, setUploadLimit] = useState(torrent.uploadLimit);
  const [downloadLimit, setDownloadLimit] = useState(torrent.downloadLimit);
  const [superSeeding, setSuperSeeding] = useState(torrent.superSeeding);
  const [forceStart, setForceStart] = useState(torrent.forceStart);
  const [sequentialDownload, setSequentialDownload] = useState(torrent.sequentialDownload);
  const [active, setActive] = useState(torrent.active);
  const [label, setLabel] = useState(torrent.label ?? '');
  const [uploadSpeed, setUploadSpeed] = useState(torrent.uploadSpeed);
  const [downloadSpeed, setDownloadSpeed] = useState(torrent.downloadSpeed);
  const [announceInterval, setAnnounceInterval] = useState(torrent.announceInterval);
  const [nextUpdate, setNextUpdate] = useState(torrent.nextUpdate);
  const [threshold, setThreshold] = useState(torrent.threshold);
  const [smallTorrentLimit, setSmallTorrentLimit] = useState(torrent.smallTorrentLimit);
  const [uploaded, setUploaded] = useState(torrent.uploaded);
  const [downloaded, setDownloaded] = useState(torrent.downloaded);
  const [sessionUploaded, setSessionUploaded] = useState(torrent.sessionUploaded);
  const [sessionDownloaded, setSessionDownloaded] = useState(torrent.sessionDownloaded);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    setPriority(String(torrent.priority));
    setUploadLimit(torrent.uploadLimit);
    setDownloadLimit(torrent.downloadLimit);
    setSuperSeeding(torrent.superSeeding);
    setForceStart(torrent.forceStart);
    setSequentialDownload(torrent.sequentialDownload);
    setActive(torrent.active);
    setLabel(torrent.label ?? '');
    setUploadSpeed(torrent.uploadSpeed);
    setDownloadSpeed(torrent.downloadSpeed);
    setAnnounceInterval(torrent.announceInterval);
    setNextUpdate(torrent.nextUpdate);
    setThreshold(torrent.threshold);
    setSmallTorrentLimit(torrent.smallTorrentLimit);
    setUploaded(torrent.uploaded);
    setDownloaded(torrent.downloaded);
    setSessionUploaded(torrent.sessionUploaded);
    setSessionDownloaded(torrent.sessionDownloaded);
    setDirty(false);
  }, [torrent]);

  const handleSave = () => {
    updateTorrent.mutate(
      {
        ...torrent,
        priority: parseInt(priority, 10),
        uploadLimit,
        downloadLimit,
        superSeeding,
        forceStart,
        sequentialDownload,
        active,
        label: label || null,
        uploadSpeed,
        downloadSpeed,
        announceInterval,
        nextUpdate,
        threshold,
        smallTorrentLimit,
        uploaded,
        downloaded,
        sessionUploaded,
        sessionDownloaded,
      },
      { onSuccess: () => setDirty(false) }
    );
  };

  const mark = <T,>(setter: (v: T) => void) => (v: T) => { setter(v); setDirty(true); };
  const numChange = (setter: (v: number) => void) => (e: React.ChangeEvent<HTMLInputElement>) => mark(setter)(parseInt(e.target.value, 10) || 0);

  return (
    <div className="detail-panel-options">
      <div className="options-section-title">Transfer</div>
      <div className="options-grid">
        <div className="form-group form-group-inline">
          <label className="form-label">Priority</label>
          <select className="form-select" value={priority} onChange={(e) => mark(setPriority)(e.target.value)}>
            {PRIORITY_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Upload Limit (KB/s)</label>
          <input type="number" className="form-input" value={uploadLimit} onChange={numChange(setUploadLimit)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Download Limit (KB/s)</label>
          <input type="number" className="form-input" value={downloadLimit} onChange={numChange(setDownloadLimit)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Upload Speed (B/s)</label>
          <input type="number" className="form-input" value={uploadSpeed} onChange={numChange(setUploadSpeed)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Download Speed (B/s)</label>
          <input type="number" className="form-input" value={downloadSpeed} onChange={numChange(setDownloadSpeed)} min={0} />
        </div>
      </div>

      <div className="options-section-title">Seeding</div>
      <div className="options-grid">
        <div className="form-group form-group-inline">
          <label className="form-label">Active</label>
          <label className="toggle-switch"><input type="checkbox" checked={active} onChange={(e) => mark(setActive)(e.target.checked)} /><span className="toggle-slider" /></label>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Super Seeding</label>
          <label className="toggle-switch"><input type="checkbox" checked={superSeeding} onChange={(e) => mark(setSuperSeeding)(e.target.checked)} /><span className="toggle-slider" /></label>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Force Start</label>
          <label className="toggle-switch"><input type="checkbox" checked={forceStart} onChange={(e) => mark(setForceStart)(e.target.checked)} /><span className="toggle-slider" /></label>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Sequential Download</label>
          <label className="toggle-switch"><input type="checkbox" checked={sequentialDownload} onChange={(e) => mark(setSequentialDownload)(e.target.checked)} /><span className="toggle-slider" /></label>
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Label</label>
          <input type="text" className="form-input" value={label} onChange={(e) => mark(setLabel)(e.target.value)} placeholder="e.g. movies" />
        </div>
      </div>

      <div className="options-section-title">Simulation</div>
      <div className="options-grid">
        <div className="form-group form-group-inline">
          <label className="form-label">Announce Interval (s)</label>
          <input type="number" className="form-input" value={announceInterval} onChange={numChange(setAnnounceInterval)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Next Update (s)</label>
          <input type="number" className="form-input" value={nextUpdate} onChange={numChange(setNextUpdate)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Threshold</label>
          <input type="number" className="form-input" value={threshold} onChange={numChange(setThreshold)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Small Torrent Limit</label>
          <input type="number" className="form-input" value={smallTorrentLimit} onChange={numChange(setSmallTorrentLimit)} min={0} />
        </div>
      </div>

      <div className="options-section-title">Totals</div>
      <div className="options-grid">
        <div className="form-group form-group-inline">
          <label className="form-label">Total Uploaded</label>
          <input type="number" className="form-input" value={uploaded} onChange={numChange(setUploaded)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Total Downloaded</label>
          <input type="number" className="form-input" value={downloaded} onChange={numChange(setDownloaded)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Session Uploaded</label>
          <input type="number" className="form-input" value={sessionUploaded} onChange={numChange(setSessionUploaded)} min={0} />
        </div>
        <div className="form-group form-group-inline">
          <label className="form-label">Session Downloaded</label>
          <input type="number" className="form-input" value={sessionDownloaded} onChange={numChange(setSessionDownloaded)} min={0} />
        </div>
      </div>

      <div className="form-actions">
        <button className="btn btn-success btn-small" onClick={handleSave} disabled={!dirty || updateTorrent.isPending}>
          {updateTorrent.isPending ? 'Saving...' : 'Save'}
        </button>
      </div>
    </div>
  );
}

const CHART_W = 400;
const CHART_H = 120;
const CHART_PAD = { top: 6, right: 10, bottom: 16, left: 50 };
const MAX_PTS = 60;

function MiniChart({ title, value, data, color }: { title: string; value: string; data: number[]; color: string }) {
  const cw = CHART_W - CHART_PAD.left - CHART_PAD.right;
  const ch = CHART_H - CHART_PAD.top - CHART_PAD.bottom;
  let maxVal = 0;
  for (const v of data) if (v > maxVal) maxVal = v;
  const niceMax = maxVal > 0 ? maxVal * 1.1 : 1;

  const pts = data.map((v, i) => {
    const x = CHART_PAD.left + (i / Math.max(1, MAX_PTS - 1)) * cw;
    const y = CHART_PAD.top + ch - (v / niceMax) * ch;
    return `${x},${y}`;
  }).join(' ');

  return (
    <div className="detail-panel-chart">
      <div className="detail-panel-chart-header">
        <span>{title}</span>
        <span style={{ color }}>{value}</span>
      </div>
      <svg width="100%" viewBox={`0 0 ${CHART_W} ${CHART_H}`} preserveAspectRatio="xMidYMid meet">
        <rect x={CHART_PAD.left} y={CHART_PAD.top} width={cw} height={ch} fill="none" stroke="var(--border-light)" strokeWidth={0.5} />
        {pts && <polyline points={pts} fill="none" stroke={color} strokeWidth={1.5} strokeLinejoin="round" />}
      </svg>
    </div>
  );
}

function MonitoringTab({ torrent }: { torrent: Torrent }) {
  const { data: history } = useTorrentSpeedHistory(torrent.id);
  const histRef = useRef<{ up: number[]; down: number[] }>({ up: [], down: [] });
  const seededRef = useRef(false);
  const prevRef = useRef<{ uploaded: number; downloaded: number; ts: number } | null>(null);
  const [, setTick] = useState(0);

  if (history && history.length > 0 && !seededRef.current) {
    seededRef.current = true;
    histRef.current.up = history.map((s) => s.uploadSpeed);
    histRef.current.down = history.map((s) => s.downloadSpeed);
  }

  useEffect(() => {
    const now = Date.now();
    const prev = prevRef.current;
    if (prev) {
      const dt = (now - prev.ts) / 1000;
      if (dt >= 1) {
        const push = (arr: number[], val: number) => {
          const next = [...arr, val];
          return next.length > MAX_PTS ? next.slice(next.length - MAX_PTS) : next;
        };
        histRef.current.up = push(histRef.current.up, Math.max(0, (torrent.uploaded - prev.uploaded) / dt));
        histRef.current.down = push(histRef.current.down, Math.max(0, (torrent.downloaded - prev.downloaded) / dt));
        setTick((t) => t + 1);
      }
    }
    prevRef.current = { uploaded: torrent.uploaded, downloaded: torrent.downloaded, ts: now };
  }, [torrent.uploaded, torrent.downloaded]);

  const h = histRef.current;
  const curUp = h.up.length > 0 ? h.up[h.up.length - 1] : 0;
  const curDown = h.down.length > 0 ? h.down[h.down.length - 1] : 0;

  return (
    <div className="detail-panel-monitoring">
      <MiniChart title="Upload" value={formatSpeed(curUp)} data={h.up} color="#c8a84e" />
      <MiniChart title="Download" value={formatSpeed(curDown)} data={h.down} color="#b5443a" />
    </div>
  );
}

function LogTab({ torrent }: { torrent: Torrent }) {
  return (
    <div className="detail-panel-grid">
      <div className="detail-panel-row">
        <span className="detail-panel-label">Added</span>
        <span className="detail-panel-value">{formatDate(torrent.dateAdded)}</span>
      </div>
      {torrent.lastActive && (
        <div className="detail-panel-row">
          <span className="detail-panel-label">Last Active</span>
          <span className="detail-panel-value">{formatDate(torrent.lastActive)} ({torrent.status})</span>
        </div>
      )}
    </div>
  );
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
  const { data: torrent, isLoading } = useTorrent(torrentId);
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const [tab, setTab] = useState<DetailTab>('status');
  const [panelHeight, setPanelHeight] = useState(() => {
    const stored = localStorage.getItem('seedarr-detail-height');
    return stored ? parseInt(stored, 10) : 280;
  });
  const panelRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ startY: number; startH: number } | null>(null);
  const dragListenersRef = useRef<{ move: (e: MouseEvent) => void; up: (e: MouseEvent) => void } | null>(null);

  useEffect(() => {
    return () => {
      if (dragListenersRef.current) {
        document.removeEventListener('mousemove', dragListenersRef.current.move);
        document.removeEventListener('mouseup', dragListenersRef.current.up);
        dragListenersRef.current = null;
      }
    };
  }, []);

  const onMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    dragRef.current = { startY: e.clientY, startH: panelHeight };

    const onMouseMove = (ev: MouseEvent) => {
      if (!dragRef.current) return;
      const delta = dragRef.current.startY - ev.clientY;
      const newH = Math.max(120, Math.min(window.innerHeight - 200, dragRef.current.startH + delta));
      setPanelHeight(newH);
    };

    const onMouseUp = () => {
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
      dragListenersRef.current = null;
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
      if (dragRef.current) {
        const finalH = panelRef.current?.offsetHeight ?? panelHeight;
        localStorage.setItem('seedarr-detail-height', String(finalH));
      }
      dragRef.current = null;
    };

    dragListenersRef.current = { move: onMouseMove, up: onMouseUp };
    document.body.style.cursor = 'row-resize';
    document.body.style.userSelect = 'none';
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
  }, [panelHeight]);

  useEffect(() => {
    localStorage.setItem('seedarr-detail-height', String(panelHeight));
  }, [panelHeight]);

  if (isLoading) return <div className="detail-panel" style={{ height: panelHeight }}><div className="detail-panel-loading">Loading...</div></div>;
  if (!torrent) return <div className="detail-panel" style={{ height: panelHeight }}><div className="detail-panel-empty">Torrent not found</div></div>;

  const isSeeding = torrent.status === 'Seeding';

  return (
    <div className="detail-panel" ref={panelRef} style={{ height: panelHeight }}>
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
