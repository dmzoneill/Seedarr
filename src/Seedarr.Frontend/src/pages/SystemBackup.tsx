import { useState } from 'react';
import { useBackups, useCreateBackup, useDeleteBackup, useRestoreBackup } from '../api/hooks';
import { useToast } from '../context/ToastContext';
import { formatBytes, formatDate } from '../utils/formatters';

function BackupIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="17 8 12 3 7 8" />
      <line x1="12" y1="3" x2="12" y2="15" />
    </svg>
  );
}

function RestoreIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="1 4 1 10 7 10" />
      <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
    </svg>
  );
}

function DownloadIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="7 10 12 15 17 10" />
      <line x1="12" y1="15" x2="12" y2="3" />
    </svg>
  );
}

function TrashIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </svg>
  );
}

function SystemBackup() {
  const { data: backups, isLoading } = useBackups();
  const createBackup = useCreateBackup();
  const deleteBackup = useDeleteBackup();
  const restoreBackup = useRestoreBackup();
  const { showToast } = useToast();

  const [confirmDelete, setConfirmDelete] = useState<number | null>(null);
  const [confirmRestore, setConfirmRestore] = useState<string | null>(null);

  const handleCreateBackup = () => {
    createBackup.mutate(undefined, {
      onSuccess: () => showToast('Backup created successfully', 'success'),
      onError: () => showToast('Failed to create backup', 'error'),
    });
  };

  const handleDeleteBackup = (id: number) => {
    deleteBackup.mutate(id, {
      onSuccess: () => {
        showToast('Backup deleted', 'success');
        setConfirmDelete(null);
      },
      onError: () => showToast('Failed to delete backup', 'error'),
    });
  };

  const handleRestoreBackup = (fileName: string) => {
    restoreBackup.mutate(fileName, {
      onSuccess: () => {
        showToast('Backup restored. Restart required.', 'info');
        setConfirmRestore(null);
      },
      onError: () => showToast('Failed to restore backup', 'error'),
    });
  };

  return (
    <div>
      <h1 className="page-heading">Backups</h1>

      <div className="toolbar">
        <button
          className="btn"
          onClick={handleCreateBackup}
          disabled={createBackup.isPending}
        >
          <BackupIcon />
          {createBackup.isPending ? 'Creating...' : 'Backup Now'}
        </button>
      </div>

      {isLoading && <p className="loading">Loading backups...</p>}

      {backups && (
        <div className="torrent-table-wrapper">
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th">Name</th>
                <th className="torrent-table-th">Size</th>
                <th className="torrent-table-th">Time</th>
                <th className="torrent-table-th">Actions</th>
              </tr>
            </thead>
            <tbody>
              {backups.length === 0 && (
                <tr>
                  <td colSpan={4} className="torrent-table-empty">
                    No backups found. Click &quot;Backup Now&quot; to create one.
                  </td>
                </tr>
              )}
              {backups.map((backup) => (
                <tr key={backup.id} className="torrent-table-row">
                  <td>
                    <a
                      href={`/api/v1/backup/${backup.id}/download`}
                      className="torrent-link"
                      download
                    >
                      <DownloadIcon /> {backup.name}
                    </a>
                  </td>
                  <td>{formatBytes(backup.size)}</td>
                  <td>{formatDate(backup.time)}</td>
                  <td>
                    <div className="torrent-actions">
                      <button
                        className="btn btn-small"
                        onClick={() => setConfirmRestore(backup.name)}
                        title="Restore"
                        disabled={restoreBackup.isPending}
                      >
                        <RestoreIcon />
                      </button>
                      <button
                        className="btn btn-small btn-danger"
                        onClick={() => setConfirmDelete(backup.id)}
                        title="Delete"
                        disabled={deleteBackup.isPending}
                      >
                        <TrashIcon />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {confirmDelete !== null && (
        <div className="modal-overlay" onClick={() => setConfirmDelete(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3 className="modal-title">Delete Backup</h3>
            <p style={{ fontSize: '0.9rem', color: 'var(--text-secondary)', marginBottom: '1rem' }}>
              Are you sure you want to delete this backup? This action cannot be undone.
            </p>
            <div className="modal-actions">
              <button className="btn" onClick={() => setConfirmDelete(null)}>
                Cancel
              </button>
              <button
                className="btn btn-danger"
                onClick={() => handleDeleteBackup(confirmDelete)}
                disabled={deleteBackup.isPending}
              >
                {deleteBackup.isPending ? 'Deleting...' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}

      {confirmRestore !== null && (
        <div className="modal-overlay" onClick={() => setConfirmRestore(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3 className="modal-title">Restore Backup</h3>
            <div className="modal-error">
              Warning: This will overwrite your current database and configuration.
              A restart will be required after restoring.
            </div>
            <p style={{ fontSize: '0.9rem', color: 'var(--text-secondary)', marginBottom: '1rem' }}>
              Are you sure you want to restore from &quot;{confirmRestore}&quot;?
            </p>
            <div className="modal-actions">
              <button className="btn" onClick={() => setConfirmRestore(null)}>
                Cancel
              </button>
              <button
                className="btn btn-danger"
                onClick={() => handleRestoreBackup(confirmRestore)}
                disabled={restoreBackup.isPending}
              >
                {restoreBackup.isPending ? 'Restoring...' : 'Restore'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default SystemBackup;
