import { useState, useRef, useCallback } from 'react';
import { useAddTorrent } from '../api/hooks';

interface AddTorrentModalProps {
  onClose: () => void;
}

type InputMode = 'file' | 'magnet';

function AddTorrentModal({ onClose }: AddTorrentModalProps) {
  const [mode, setMode] = useState<InputMode>('file');
  const [file, setFile] = useState<File | null>(null);
  const [magnetLink, setMagnetLink] = useState('');
  const [isDragOver, setIsDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const addTorrent = useAddTorrent();

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    const droppedFile = e.dataTransfer.files[0];
    if (droppedFile && droppedFile.name.endsWith('.torrent')) {
      setFile(droppedFile);
    }
  }, []);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const selected = e.target.files?.[0];
    if (selected) {
      setFile(selected);
    }
  };

  const handleSubmit = () => {
    if (mode === 'file' && file) {
      addTorrent.mutate(
        { file },
        { onSuccess: () => onClose() }
      );
    } else if (mode === 'magnet' && magnetLink.trim()) {
      addTorrent.mutate(
        { magnetLink: magnetLink.trim() },
        { onSuccess: () => onClose() }
      );
    }
  };

  const canSubmit =
    (mode === 'file' && file !== null) ||
    (mode === 'magnet' && magnetLink.trim().startsWith('magnet:?'));

  return (
    <div className="modal-overlay" onClick={handleBackdropClick}>
      <div className="modal">
        <h2 className="modal-title">Add Torrent</h2>

        <div className="tab-nav">
          <button
            className={`tab-btn ${mode === 'file' ? 'tab-btn-active' : ''}`}
            onClick={() => setMode('file')}
          >
            Torrent File
          </button>
          <button
            className={`tab-btn ${mode === 'magnet' ? 'tab-btn-active' : ''}`}
            onClick={() => setMode('magnet')}
          >
            Magnet Link
          </button>
        </div>

        {mode === 'file' && (
          <>
            <div
              className={`drop-zone ${isDragOver ? 'drop-zone-active' : ''} ${file ? 'drop-zone-has-file' : ''}`}
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => fileInputRef.current?.click()}
            >
              {file ? (
                <span className="drop-zone-filename">{file.name}</span>
              ) : (
                <span className="drop-zone-prompt">
                  Drop a .torrent file here or click to browse
                </span>
              )}
            </div>
            <input
              ref={fileInputRef}
              type="file"
              accept=".torrent"
              onChange={handleFileChange}
              style={{ display: 'none' }}
            />
          </>
        )}

        {mode === 'magnet' && (
          <input
            type="text"
            className="search-input modal-magnet-input"
            placeholder="magnet:?xt=urn:btih:..."
            value={magnetLink}
            onChange={(e) => setMagnetLink(e.target.value)}
            autoFocus
          />
        )}

        {addTorrent.isError && (
          <div className="modal-error">
            {addTorrent.error instanceof Error
              ? addTorrent.error.message
              : 'Failed to add torrent'}
          </div>
        )}

        <div className="modal-actions">
          <button className="btn" onClick={onClose} disabled={addTorrent.isPending}>
            Cancel
          </button>
          <button
            className="btn btn-success"
            onClick={handleSubmit}
            disabled={!canSubmit || addTorrent.isPending}
          >
            {addTorrent.isPending ? 'Adding...' : 'Add'}
          </button>
        </div>
      </div>
    </div>
  );
}

export default AddTorrentModal;
