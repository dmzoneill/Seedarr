import { useState } from "react";
import {
  useBackups,
  useCreateBackup,
  useDeleteBackup,
  useRestoreBackup,
} from "../api/hooks";
import { apiClient } from "../api/client";
import { usePermissions } from "../hooks/usePermissions";
import { useToast } from "../context/ToastContext";
import { formatBytes, formatDate } from "../utils/formatters";
import { trackSystemMaintenanceAction } from "../utils/analytics";

function BackupIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="17 8 12 3 7 8" />
      <line x1="12" y1="3" x2="12" y2="15" />
    </svg>
  );
}

function RestoreIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="1 4 1 10 7 10" />
      <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
    </svg>
  );
}

function DownloadIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="7 10 12 15 17 10" />
      <line x1="12" y1="15" x2="12" y2="3" />
    </svg>
  );
}

function TrashIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </svg>
  );
}

function SystemBackup() {
  const { canManageBackups } = usePermissions();
  const { data: backups, isLoading, isError } = useBackups();
  const createBackup = useCreateBackup();
  const deleteBackup = useDeleteBackup();
  const restoreBackup = useRestoreBackup();
  const { showToast } = useToast();

  const [confirmDelete, setConfirmDelete] = useState<number | null>(null);
  const [confirmRestore, setConfirmRestore] = useState<string | null>(null);

  const handleDownload = async (backupId: number, fileName: string) => {
    try {
      trackSystemMaintenanceAction("backup_download");
      const blob = await apiClient.get<Blob>(`/backup/${backupId}/download`, {
        responseType: "blob",
      });
      const url = window.URL.createObjectURL(new Blob([blob]));
      const a = document.createElement("a");
      a.href = url;
      a.download = fileName;
      a.click();
      window.URL.revokeObjectURL(url);
    } catch {
      showToast("Failed to download backup", "error");
    }
  };

  const handleCreateBackup = () => {
    trackSystemMaintenanceAction("backup_create");
    createBackup.mutate(undefined, {
      onSuccess: () => showToast("Backup created successfully", "success"),
      onError: () => showToast("Failed to create backup", "error"),
    });
  };

  const handleDeleteBackup = (id: number) => {
    trackSystemMaintenanceAction("backup_delete");
    deleteBackup.mutate(id, {
      onSuccess: () => {
        showToast("Backup deleted", "success");
        setConfirmDelete(null);
      },
      onError: () => showToast("Failed to delete backup", "error"),
    });
  };

  const handleRestoreBackup = (fileName: string) => {
    trackSystemMaintenanceAction("backup_restore");
    restoreBackup.mutate(fileName, {
      onSuccess: () => {
        showToast("Backup restored. Restart required.", "info");
        setConfirmRestore(null);
      },
      onError: () => showToast("Failed to restore backup", "error"),
    });
  };

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {!canManageBackups && (
        <div
          role="alert"
          style={{
            padding: "0.75rem 1rem",
            marginBottom: "1rem",
            backgroundColor: "rgba(239, 68, 68, 0.1)",
            border: "1px solid rgba(239, 68, 68, 0.3)",
            borderRadius: "6px",
            color: "#fca5a5",
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            fontSize: "0.875rem",
          }}
        >
          <span>🔒</span>
          <span>You have ReadOnly permissions. Backup creation and restoration require Admin privileges.</span>
        </div>
      )}
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>💾</span> System: Backups
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Create, download, and restore Seedarr configuration and database snapshots
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          <button
            className="btn btn-primary"
            onClick={handleCreateBackup}
            disabled={createBackup.isPending || !canManageBackups}
            title={!canManageBackups ? "Creating backups requires Admin privileges" : undefined}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            <BackupIcon />
            <span>
              {createBackup.isPending ? "Creating Backup..." : "Backup Now"}
            </span>
          </button>
        </div>
      </div>

      {isLoading && <p className="loading">Loading backups...</p>}
      {!isLoading && isError && (
        <p className="error">Failed to load backups.</p>
      )}

      {/* Backups List Card */}
      <div
        className="card"
        style={{
          borderRadius: "8px",
          border: "1px solid var(--border-light)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            padding: "1.1rem 1.25rem 0.85rem",
            borderBottom: "1px solid var(--border-light)",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              margin: 0,
            }}
          >
            Stored Backup Snapshots
          </h2>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            ZIP archives containing the SQLite database and application settings
          </div>
        </div>

        {backups && (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Archive Name</th>
                  <th className="torrent-table-th">File Size</th>
                  <th className="torrent-table-th">Creation Date</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {backups.length === 0 && (
                  <tr>
                    <td colSpan={4} className="torrent-table-empty">
                      No backups found. Click &quot;Backup Now&quot; to create a
                      snapshot.
                    </td>
                  </tr>
                )}
                {backups.map((backup) => (
                  <tr key={backup.id} className="torrent-table-row">
                    <td>
                      <button
                        type="button"
                        onClick={() => handleDownload(backup.id, backup.name)}
                        className="torrent-link"
                        style={{
                          background: "none",
                          border: "none",
                          padding: 0,
                          cursor: "pointer",
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.4rem",
                          fontWeight: 500,
                          font: "inherit",
                        }}
                      >
                        <DownloadIcon /> {backup.name}
                      </button>
                    </td>
                    <td>{formatBytes(backup.size)}</td>
                    <td>{formatDate(backup.time)}</td>
                    <td style={{ textAlign: "right" }}>
                      {canManageBackups ? (
                        <div
                          className="torrent-actions"
                          style={{ display: "inline-flex", gap: "0.4rem" }}
                        >
                          <button
                            className="btn btn-small btn-outline"
                            onClick={() => setConfirmRestore(backup.name)}
                            title="Restore Snapshot"
                            disabled={restoreBackup.isPending}
                          >
                            <RestoreIcon />
                          </button>
                          <button
                            className="btn btn-small btn-danger"
                            onClick={() => setConfirmDelete(backup.id)}
                            title="Delete Snapshot"
                            disabled={deleteBackup.isPending}
                          >
                            <TrashIcon />
                          </button>
                        </div>
                      ) : (
                        <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                          🔒 Read Only
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Delete Confirmation Modal */}
      {confirmDelete !== null && (
        <div className="modal-overlay" onClick={() => setConfirmDelete(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 450,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <h3
              className="modal-title"
              style={{ fontSize: "1.15rem", marginBottom: "0.75rem" }}
            >
              Delete Backup Snapshot
            </h3>
            <p
              style={{
                fontSize: "0.875rem",
                color: "var(--text-secondary)",
                marginBottom: "1.25rem",
                lineHeight: 1.5,
              }}
            >
              Are you sure you want to permanently delete this backup archive?
              This action cannot be undone.
            </p>
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                className="btn btn-outline btn-small"
                onClick={() => setConfirmDelete(null)}
              >
                Cancel
              </button>
              <button
                className="btn btn-danger btn-small"
                onClick={() => handleDeleteBackup(confirmDelete)}
                disabled={deleteBackup.isPending}
              >
                {deleteBackup.isPending ? "Deleting..." : "Delete Backup"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Restore Confirmation Modal */}
      {confirmRestore !== null && (
        <div className="modal-overlay" onClick={() => setConfirmRestore(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 500,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <h3
              className="modal-title"
              style={{ fontSize: "1.15rem", marginBottom: "0.75rem" }}
            >
              Restore Backup Snapshot
            </h3>
            <div
              style={{
                padding: "0.75rem 1rem",
                borderRadius: "6px",
                backgroundColor: "rgba(220, 53, 69, 0.15)",
                border: "1px solid rgba(220, 53, 69, 0.35)",
                color: "var(--danger, #dc3545)",
                fontSize: "0.85rem",
                marginBottom: "1rem",
                lineHeight: 1.4,
              }}
            >
              ⚠️ Warning: Restoring will replace your current database and
              configuration. An application restart will be required immediately
              afterwards.
            </div>
            <p
              style={{
                fontSize: "0.875rem",
                color: "var(--text-secondary)",
                marginBottom: "1.25rem",
                lineHeight: 1.5,
              }}
            >
              Are you sure you want to restore from &quot;
              <strong>{confirmRestore}</strong>&quot;?
            </p>
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
              }}
            >
              <button
                className="btn btn-outline btn-small"
                onClick={() => setConfirmRestore(null)}
              >
                Cancel
              </button>
              <button
                className="btn btn-danger btn-small"
                onClick={() => handleRestoreBackup(confirmRestore)}
                disabled={restoreBackup.isPending}
              >
                {restoreBackup.isPending ? "Restoring..." : "Confirm & Restore"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default SystemBackup;
