import { useState, useEffect } from 'react';
import { Torrent } from '../../api/types';
import { useUpdateTorrent } from '../../api/hooks';

const priorityOptions = [
  { value: '0', label: 'Low' },
  { value: '1', label: 'Normal' },
  { value: '2', label: 'High' },
];

export function OptionsTab({ torrent }: { torrent: Torrent }) {
  const updateTorrent = useUpdateTorrent();
  const [priority, setPriority] = useState(String(torrent.priority));
  const [uploadLimit, setUploadLimit] = useState(torrent.uploadLimit);
  const [downloadLimit, setDownloadLimit] = useState(torrent.downloadLimit);
  const [superSeeding, setSuperSeeding] = useState(torrent.superSeeding);
  const [forceStart, setForceStart] = useState(torrent.forceStart);
  const [label, setLabel] = useState(torrent.label ?? '');
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (dirty) return;
    setPriority(String(torrent.priority));
    setUploadLimit(torrent.uploadLimit);
    setDownloadLimit(torrent.downloadLimit);
    setSuperSeeding(torrent.superSeeding);
    setForceStart(torrent.forceStart);
    setLabel(torrent.label ?? '');
  }, [torrent, dirty]);

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
          <span className="form-hint">KB/s, 0 = use global limit</span>
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
          <span className="form-hint">KB/s, 0 = use global limit</span>
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
            Failed to save: {updateTorrent.error?.message ?? 'Unknown error'}
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
