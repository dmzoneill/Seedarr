import { useState, useEffect, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useTorrent, useTorrentFiles, useTorrentTrackers, useStartSeeding, useStopSeeding, useUpdateTorrent } from '../api/hooks';
import { formatBytes, formatSpeed, formatRatio, formatDate } from '../utils/formatters';
import { SkeletonLine } from '../components/Skeleton';
import PeerList from '../components/PeerList';

type Tab = 'general' | 'files' | 'trackers' | 'options' | 'peers' | 'monitoring' | 'log';

function GeneralTab({ torrent }: { torrent: import('../api/types').Torrent }) {
  return (
    <div className="detail-grid">
      <div className="card">
        <h3>Info</h3>
        <div className="status-row">
          <span className="status-label">Name</span>
          <span className="status-value">{torrent.name}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Info Hash</span>
          <span className="status-value mono">{torrent.infoHash}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Size</span>
          <span className="status-value">{formatBytes(torrent.totalSize)}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Pieces</span>
          <span className="status-value">{torrent.pieceCount} x {formatBytes(torrent.pieceLength)}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Private</span>
          <span className="status-value">{torrent.isPrivate ? 'Yes' : 'No'}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Created</span>
          <span className="status-value">{formatDate(torrent.creationDate)}</span>
        </div>
        {torrent.createdBy && (
          <div className="status-row">
            <span className="status-label">Created By</span>
            <span className="status-value">{torrent.createdBy}</span>
          </div>
        )}
        {torrent.comment && (
          <div className="status-row">
            <span className="status-label">Comment</span>
            <span className="status-value">{torrent.comment}</span>
          </div>
        )}
        {torrent.sourcePath && (
          <div className="status-row">
            <span className="status-label">Source Path</span>
            <span className="status-value mono">{torrent.sourcePath}</span>
          </div>
        )}
      </div>

      <div className="card">
        <h3>Stats</h3>
        <div className="status-row">
          <span className="status-label">Status</span>
          <span className="status-value">
            <span className={`badge badge-${torrent.status.toLowerCase()}`}>{torrent.status}</span>
          </span>
        </div>
        <div className="status-row">
          <span className="status-label">Uploaded</span>
          <span className="status-value">{formatBytes(torrent.uploaded)}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Downloaded</span>
          <span className="status-value">{formatBytes(torrent.downloaded)}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Ratio</span>
          <span className="status-value">{formatRatio(torrent.ratio)}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Seeders</span>
          <span className="status-value">{torrent.seeders}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Leechers</span>
          <span className="status-value">{torrent.leechers}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Added</span>
          <span className="status-value">{formatDate(torrent.dateAdded)}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Last Active</span>
          <span className="status-value">{formatDate(torrent.lastActive)}</span>
        </div>
      </div>
    </div>
  );
}

interface FileTreeNode {
  name: string;
  path: string;
  size: number;
  isDir: boolean;
  children: FileTreeNode[];
  fileId?: number;
}

function buildFileTree(files: import('../api/types').TorrentFileInfo[]): FileTreeNode[] {
  const root: FileTreeNode[] = [];

  for (const file of files) {
    const parts = file.path.split('/');
    let current = root;

    for (let i = 0; i < parts.length; i++) {
      const part = parts[i];
      const isLast = i === parts.length - 1;
      let existing = current.find((n) => n.name === part && n.isDir === !isLast);

      if (!existing) {
        existing = {
          name: part,
          path: parts.slice(0, i + 1).join('/'),
          size: isLast ? file.size : 0,
          isDir: !isLast,
          children: [],
          fileId: isLast ? file.id : undefined,
        };
        current.push(existing);
      }

      if (!isLast) {
        existing.size += file.size;
        current = existing.children;
      }
    }
  }

  return root;
}

function FileTreeRow({ node, depth, expanded, onToggle }: {
  node: FileTreeNode;
  depth: number;
  expanded: Set<string>;
  onToggle: (path: string) => void;
}) {
  const isOpen = expanded.has(node.path);
  const indent = depth * 20;

  return (
    <>
      <tr className="torrent-table-row" style={{ cursor: node.isDir ? 'pointer' : 'default' }} onClick={() => node.isDir && onToggle(node.path)}>
        <td className="mono" style={{ paddingLeft: indent + 8 }}>
          {node.isDir ? (
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
              <span style={{ fontSize: 10, width: 12, textAlign: 'center' }}>{isOpen ? '▼' : '▶'}</span>
              <span style={{ opacity: 0.7 }}>📁</span> {node.name}/
            </span>
          ) : (
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
              <span style={{ width: 12 }} />
              <span style={{ opacity: 0.7 }}>📄</span> {node.name}
            </span>
          )}
        </td>
        <td>{formatBytes(node.size)}</td>
      </tr>
      {node.isDir && isOpen && node.children
        .sort((a, b) => (a.isDir === b.isDir ? a.name.localeCompare(b.name) : a.isDir ? -1 : 1))
        .map((child) => (
          <FileTreeRow key={child.path} node={child} depth={depth + 1} expanded={expanded} onToggle={onToggle} />
        ))}
    </>
  );
}

function FilesTab({ torrent }: { torrent: import('../api/types').Torrent }) {
  const { data: files, isLoading, error } = useTorrentFiles(torrent.id);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  function toggleDir(path: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }

  function expandAll() {
    if (!files) return;
    const dirs = new Set<string>();
    for (const f of files) {
      const parts = f.path.split('/');
      for (let i = 1; i < parts.length; i++) {
        dirs.add(parts.slice(0, i).join('/'));
      }
    }
    setExpanded(dirs);
  }

  const tree = files ? buildFileTree(files) : [];
  const hasDirectories = tree.some((n) => n.isDir);

  return (
    <div className="card">
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h3>Files ({files?.length ?? 0})</h3>
        {hasDirectories && (
          <div style={{ display: 'flex', gap: 4 }}>
            <button className="btn btn-sm btn-default" onClick={expandAll}>Expand All</button>
            <button className="btn btn-sm btn-default" onClick={() => setExpanded(new Set())}>Collapse All</button>
          </div>
        )}
      </div>
      {isLoading && (
        <div className="torrent-table-wrapper">
          <SkeletonLine width="100%" height="2rem" />
          <SkeletonLine width="100%" height="1.5rem" />
          <SkeletonLine width="100%" height="1.5rem" />
        </div>
      )}
      {error && <p className="error">Failed to load files.</p>}
      {files && files.length === 0 && (
        <p className="torrent-table-empty">No files found</p>
      )}
      {tree.length > 0 && (
        <div className="torrent-table-wrapper">
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th">Path</th>
                <th className="torrent-table-th">Size</th>
              </tr>
            </thead>
            <tbody>
              {tree
                .sort((a, b) => (a.isDir === b.isDir ? a.name.localeCompare(b.name) : a.isDir ? -1 : 1))
                .map((node) => (
                  <FileTreeRow key={node.path} node={node} depth={0} expanded={expanded} onToggle={toggleDir} />
                ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function trackerStatusBadgeClass(status: string): string {
  switch (status) {
    case 'Working': return 'badge-seeding';
    case 'Announcing': return 'badge-announcing';
    case 'Failed': return 'badge-error';
    case 'Disabled': return 'badge-stopped';
    case 'Unknown':
    default: return 'badge-warning';
  }
}

function TrackersTab({ torrent }: { torrent: import('../api/types').Torrent }) {
  const { data: trackers, isLoading, error } = useTorrentTrackers(torrent.id);

  return (
    <div className="card">
      <h3>Trackers</h3>
      {isLoading && (
        <div className="torrent-table-wrapper">
          <SkeletonLine width="100%" height="2rem" />
          <SkeletonLine width="100%" height="1.5rem" />
          <SkeletonLine width="100%" height="1.5rem" />
        </div>
      )}
      {error && <p className="error">Failed to load trackers.</p>}
      {trackers && trackers.length === 0 && (
        <p className="torrent-table-empty">No trackers configured</p>
      )}
      {trackers && trackers.length > 0 && (
        <div className="torrent-table-wrapper">
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
                <th className="torrent-table-th">Next Announce</th>
              </tr>
            </thead>
            <tbody>
              {trackers.map((tracker) => (
                <tr key={tracker.id} className="torrent-table-row">
                  <td className="mono">{tracker.url}</td>
                  <td>{tracker.tier}</td>
                  <td>
                    <span className={`badge ${trackerStatusBadgeClass(tracker.status)}`}>
                      {tracker.status}
                    </span>
                  </td>
                  <td>{tracker.seeders}</td>
                  <td>{tracker.leechers}</td>
                  <td>{tracker.successfulAnnounces}/{tracker.totalAnnounces}</td>
                  <td>{formatDate(tracker.lastAnnounce)}</td>
                  <td>{formatDate(tracker.nextAnnounce)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

const priorityOptions = [
  { value: '0', label: 'Low' },
  { value: '1', label: 'Normal' },
  { value: '2', label: 'High' },
];

function OptionsTab({ torrent }: { torrent: import('../api/types').Torrent }) {
  const updateTorrent = useUpdateTorrent();
  const [priority, setPriority] = useState(String(torrent.priority));
  const [uploadLimit, setUploadLimit] = useState(torrent.uploadLimit);
  const [downloadLimit, setDownloadLimit] = useState(torrent.downloadLimit);
  const [superSeeding, setSuperSeeding] = useState(torrent.superSeeding);
  const [forceStart, setForceStart] = useState(torrent.forceStart);
  const [label, setLabel] = useState(torrent.label ?? '');
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    setPriority(String(torrent.priority));
    setUploadLimit(torrent.uploadLimit);
    setDownloadLimit(torrent.downloadLimit);
    setSuperSeeding(torrent.superSeeding);
    setForceStart(torrent.forceStart);
    setLabel(torrent.label ?? '');
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
        label: label || null,
      },
      { onSuccess: () => setDirty(false) }
    );
  };

  const mark = <T,>(setter: (v: T) => void) => (v: T) => { setter(v); setDirty(true); };

  return (
    <div className="card">
      <h3>Options</h3>
      <div className="form-group">
        <label className="form-label">
          Priority
          <span className="form-hint">Torrent priority level</span>
        </label>
        <select
          className="form-select"
          value={priority}
          onChange={(e) => mark(setPriority)(e.target.value)}
        >
          {priorityOptions.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
      </div>
      <div className="form-group">
        <label className="form-label">
          Upload Speed Limit
          <span className="form-hint">KB/s, 0 = unlimited</span>
        </label>
        <input
          type="number"
          className="form-input"
          value={uploadLimit}
          onChange={(e) => mark(setUploadLimit)(parseInt(e.target.value, 10) || 0)}
          min={0}
        />
      </div>
      <div className="form-group">
        <label className="form-label">
          Download Speed Limit
          <span className="form-hint">KB/s, 0 = unlimited</span>
        </label>
        <input
          type="number"
          className="form-input"
          value={downloadLimit}
          onChange={(e) => mark(setDownloadLimit)(parseInt(e.target.value, 10) || 0)}
          min={0}
        />
      </div>
      <div className="form-group">
        <label className="form-label">
          Super Seeding
          <span className="form-hint">Enable super seeding mode</span>
        </label>
        <label className="toggle-switch">
          <input type="checkbox" checked={superSeeding} onChange={(e) => mark(setSuperSeeding)(e.target.checked)} />
          <span className="toggle-slider" />
        </label>
      </div>
      <div className="form-group">
        <label className="form-label">
          Force Start
          <span className="form-hint">Bypass queue and start immediately</span>
        </label>
        <label className="toggle-switch">
          <input type="checkbox" checked={forceStart} onChange={(e) => mark(setForceStart)(e.target.checked)} />
          <span className="toggle-slider" />
        </label>
      </div>
      <div className="form-group">
        <label className="form-label">
          Label
          <span className="form-hint">Optional label for organization</span>
        </label>
        <input
          type="text"
          className="form-input"
          value={label}
          onChange={(e) => mark(setLabel)(e.target.value)}
          placeholder="e.g. movies, music"
        />
      </div>
      <div className="form-actions">
        <button className="btn btn-success" onClick={handleSave} disabled={!dirty || updateTorrent.isPending}>
          {updateTorrent.isPending ? 'Saving...' : 'Save'}
        </button>
        {updateTorrent.isError && (
          <span className="error" style={{ marginLeft: '0.75rem', fontSize: '0.85rem' }}>
            Failed to save: {updateTorrent.error?.message}
          </span>
        )}
        {updateTorrent.isSuccess && !dirty && (
          <span style={{ marginLeft: '0.75rem', fontSize: '0.85rem', color: 'var(--success)' }}>
            Saved
          </span>
        )}
      </div>
    </div>
  );
}

const MONITOR_MAX_POINTS = 60;
const MONITOR_CHART_WIDTH = 480;
const MONITOR_CHART_HEIGHT = 160;
const MONITOR_PADDING = { top: 8, right: 12, bottom: 20, left: 55 };

function getMonitorNiceMax(value: number): number {
  if (value <= 0) return 1;
  const magnitude = Math.pow(10, Math.floor(Math.log10(value)));
  const normalized = value / magnitude;
  let nice: number;
  if (normalized <= 1) nice = 1;
  else if (normalized <= 2) nice = 2;
  else if (normalized <= 5) nice = 5;
  else nice = 10;
  return nice * magnitude;
}

interface SpeedChartProps {
  title: string;
  value: string;
  data: number[];
  color: string;
}

function SpeedChart({ title, value, data, color }: SpeedChartProps) {
  const chartW = MONITOR_CHART_WIDTH - MONITOR_PADDING.left - MONITOR_PADDING.right;
  const chartH = MONITOR_CHART_HEIGHT - MONITOR_PADDING.top - MONITOR_PADDING.bottom;

  let maxVal = 0;
  for (const v of data) {
    if (v > maxVal) maxVal = v;
  }
  const niceMax = getMonitorNiceMax(maxVal > 0 ? maxVal * 1.1 : 1);

  const gridLineCount = 3;
  const gridLines = Array.from({ length: gridLineCount + 1 }, (_, i) => {
    const val = (niceMax / gridLineCount) * i;
    const y = MONITOR_PADDING.top + chartH - (val / niceMax) * chartH;
    return { value: val, y };
  });

  const points = data.length === 0
    ? ''
    : data
        .map((v, i) => {
          const x = MONITOR_PADDING.left + (i / Math.max(1, MONITOR_MAX_POINTS - 1)) * chartW;
          const y = MONITOR_PADDING.top + chartH - (v / niceMax) * chartH;
          return `${x},${y}`;
        })
        .join(' ');

  const areaPath = data.length < 2
    ? ''
    : (() => {
        const first = MONITOR_PADDING.left;
        const last = MONITOR_PADDING.left + ((data.length - 1) / Math.max(1, MONITOR_MAX_POINTS - 1)) * chartW;
        const bottom = MONITOR_PADDING.top + chartH;
        const linePoints = data
          .map((v, i) => {
            const x = MONITOR_PADDING.left + (i / Math.max(1, MONITOR_MAX_POINTS - 1)) * chartW;
            const y = MONITOR_PADDING.top + chartH - (v / niceMax) * chartH;
            return `${x},${y}`;
          })
          .join(' ');
        return `M${first},${bottom} L${linePoints} L${last},${bottom} Z`;
      })();

  return (
    <div className="monitoring-tile">
      <div className="monitoring-tile-title">{title}</div>
      <div className="monitoring-tile-value" style={{ color }}>{value}</div>
      <svg
        width="100%"
        viewBox={`0 0 ${MONITOR_CHART_WIDTH} ${MONITOR_CHART_HEIGHT}`}
        preserveAspectRatio="xMidYMid meet"
      >
        {gridLines.map(({ value: val, y }, i) => (
          <g key={i}>
            <line
              x1={MONITOR_PADDING.left}
              y1={y}
              x2={MONITOR_CHART_WIDTH - MONITOR_PADDING.right}
              y2={y}
              stroke="var(--border-light)"
              strokeWidth={0.5}
            />
            <text
              x={MONITOR_PADDING.left - 4}
              y={y + 3}
              textAnchor="end"
              fill="var(--text-dim)"
              fontSize={8}
              fontFamily="inherit"
            >
              {formatSpeed(val)}
            </text>
          </g>
        ))}
        <text
          x={MONITOR_PADDING.left}
          y={MONITOR_CHART_HEIGHT - 4}
          fill="var(--text-dim)"
          fontSize={8}
          textAnchor="start"
        >
          5m ago
        </text>
        <text
          x={MONITOR_CHART_WIDTH - MONITOR_PADDING.right}
          y={MONITOR_CHART_HEIGHT - 4}
          fill="var(--text-dim)"
          fontSize={8}
          textAnchor="end"
        >
          now
        </text>
        <rect
          x={MONITOR_PADDING.left}
          y={MONITOR_PADDING.top}
          width={chartW}
          height={chartH}
          fill="none"
          stroke="var(--border-light)"
          strokeWidth={0.5}
        />
        {areaPath && (
          <path d={areaPath} fill={color} opacity={0.1} />
        )}
        {points && (
          <polyline
            points={points}
            fill="none"
            stroke={color}
            strokeWidth={1.5}
            strokeLinejoin="round"
            strokeLinecap="round"
          />
        )}
      </svg>
    </div>
  );
}

function MonitoringTab({ torrent }: { torrent: import('../api/types').Torrent }) {
  const historyRef = useRef<{ uploadSpeed: number[]; downloadSpeed: number[] }>({
    uploadSpeed: [],
    downloadSpeed: [],
  });

  const prevRef = useRef<{
    uploaded: number;
    downloaded: number;
    timestamp: number;
  } | null>(null);

  // Force re-render so chart data is visible after ref update
  const [, setTick] = useState(0);

  useEffect(() => {
    const now = Date.now();
    const prev = prevRef.current;
    const h = historyRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 1) {
        const upSpeed = Math.max(0, (torrent.uploaded - prev.uploaded) / timeDelta);
        const downSpeed = Math.max(0, (torrent.downloaded - prev.downloaded) / timeDelta);

        const push = (arr: number[], val: number) => {
          const next = [...arr, val];
          if (next.length > MONITOR_MAX_POINTS) next.splice(0, next.length - MONITOR_MAX_POINTS);
          return next;
        };

        h.uploadSpeed = push(h.uploadSpeed, upSpeed);
        h.downloadSpeed = push(h.downloadSpeed, downSpeed);
        setTick((t) => t + 1);
      }
    }

    prevRef.current = {
      uploaded: torrent.uploaded,
      downloaded: torrent.downloaded,
      timestamp: now,
    };
  }, [torrent.uploaded, torrent.downloaded]);

  const h = historyRef.current;
  const currentUpload = h.uploadSpeed.length > 0 ? h.uploadSpeed[h.uploadSpeed.length - 1] : 0;
  const currentDownload = h.downloadSpeed.length > 0 ? h.downloadSpeed[h.downloadSpeed.length - 1] : 0;

  return (
    <div className="card">
      <h3>Monitoring</h3>
      <div className="monitoring-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))' }}>
        <SpeedChart
          title="Upload Speed"
          value={formatSpeed(currentUpload)}
          data={h.uploadSpeed}
          color="#c8a84e"
        />
        <SpeedChart
          title="Download Speed"
          value={formatSpeed(currentDownload)}
          data={h.downloadSpeed}
          color="#b5443a"
        />
      </div>
      <div className="detail-grid" style={{ marginTop: '1rem' }}>
        <div className="status-row">
          <span className="status-label">Total Uploaded</span>
          <span className="status-value">{formatBytes(torrent.uploaded)}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Total Downloaded</span>
          <span className="status-value">{formatBytes(torrent.downloaded)}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Ratio</span>
          <span className="status-value">{formatRatio(torrent.ratio)}</span>
        </div>
      </div>
    </div>
  );
}

function LogTab({ torrent }: { torrent: import('../api/types').Torrent }) {
  const events = [
    { time: torrent.dateAdded, event: 'Torrent added' },
    ...(torrent.lastActive
      ? [{ time: torrent.lastActive, event: `Last active (status: ${torrent.status})` }]
      : []),
  ];

  return (
    <div className="card">
      <h3>Log</h3>
      <p style={{ color: 'var(--text-dim)', marginBottom: '1rem', fontSize: '0.9rem' }}>
        Per-torrent logging coming soon. Key events for this torrent are shown below.
      </p>
      <div className="torrent-table-wrapper">
        <table className="torrent-table">
          <thead>
            <tr>
              <th className="torrent-table-th">Time</th>
              <th className="torrent-table-th">Event</th>
            </tr>
          </thead>
          <tbody>
            {events.map((entry, i) => (
              <tr key={i} className="torrent-table-row">
                <td>{formatDate(entry.time)}</td>
                <td>{entry.event}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div style={{ marginTop: '1rem' }}>
        <div className="status-row">
          <span className="status-label">Info Hash</span>
          <span className="status-value mono">{torrent.infoHash}</span>
        </div>
        <div className="status-row">
          <span className="status-label">Current Status</span>
          <span className="status-value">
            <span className={`badge badge-${torrent.status.toLowerCase()}`}>{torrent.status}</span>
          </span>
        </div>
      </div>
    </div>
  );
}

function TorrentDetails() {
  const { id } = useParams<{ id: string }>();
  const torrentId = Number(id) || 0;
  const { data: torrent, isLoading, error } = useTorrent(torrentId);
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const [activeTab, setActiveTab] = useState<Tab>('general');

  const tabs: { key: Tab; label: string }[] = [
    { key: 'general', label: 'General' },
    { key: 'files', label: 'Files' },
    { key: 'trackers', label: 'Trackers' },
    { key: 'options', label: 'Options' },
    { key: 'peers', label: 'Peers' },
    { key: 'monitoring', label: 'Monitoring' },
    { key: 'log', label: 'Log' },
  ];

  if (isLoading) {
    return (
      <div>
        <Link to="/torrents" className="back-link">Back to Torrents</Link>
        <SkeletonLine width="40%" height="1.5rem" />
        <div className="detail-grid" style={{ marginTop: '1.5rem' }}>
          <div className="card">
            <SkeletonLine width="30%" height="1rem" />
            {[0, 1, 2, 3, 4].map((i) => (
              <div key={i} className="status-row">
                <SkeletonLine width="25%" height="0.85rem" />
                <SkeletonLine width="40%" height="0.85rem" />
              </div>
            ))}
          </div>
          <div className="card">
            <SkeletonLine width="30%" height="1rem" />
            {[0, 1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="status-row">
                <SkeletonLine width="25%" height="0.85rem" />
                <SkeletonLine width="40%" height="0.85rem" />
              </div>
            ))}
          </div>
        </div>
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
