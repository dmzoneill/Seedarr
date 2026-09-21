import React, { useState, useEffect, useRef } from "react";
import { useTranslation } from "../i18n";
import { useModalRegistration } from "./ModalProvider";
import { usePermissions } from "../hooks/usePermissions";

export interface DeleteTorrentModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: (deleteFiles: boolean) => Promise<void> | void;
  torrentName?: string;
  count?: number;
  isPending?: boolean;
}

export function DeleteTorrentModal({
  isOpen,
  onClose,
  onConfirm,
  torrentName,
  count = 1,
  isPending = false,
}: DeleteTorrentModalProps) {
  const { t } = useTranslation();
  const { canDeleteTorrent } = usePermissions();
  const [deleteFiles, setDeleteFiles] = useState(false);
  const modalRef = useRef<HTMLDivElement>(null);
  const confirmButtonRef = useRef<HTMLButtonElement>(null);

  useModalRegistration({
    id: "delete-torrent-modal",
    isOpen,
    onClose: () => {
      if (!isPending) {
        onClose();
      }
    },
    modalRef,
  });

  useEffect(() => {
    if (isOpen) {
      setDeleteFiles(false);
      const timer = setTimeout(() => {
        confirmButtonRef.current?.focus();
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  if (!isOpen) {
    return null;
  }

  const isMultiple = count > 1 || (!torrentName && count !== 1);

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget && !isPending) {
      onClose();
    }
  };

  const handleConfirm = async () => {
    if (isPending) return;
    await onConfirm(deleteFiles);
  };

  return (
    <div
      ref={modalRef}
      className="modal-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="delete-torrent-modal-title"
      aria-describedby="delete-torrent-modal-desc"
    >
      <div
        className="modal"
        style={{
          maxWidth: "480px",
          width: "90%",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <h3
          id="delete-torrent-modal-title"
          className="modal-title"
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            color: "var(--danger, #ef4444)",
            marginBottom: "1rem",
          }}
        >
          <span>⚠️</span>
          <span>
            {isMultiple
              ? t("torrents.deleteTorrentsTitle", undefined, "Delete Torrents")
              : t("torrents.deleteTorrentTitle", undefined, "Delete Torrent")}
          </span>
        </h3>

        {!canDeleteTorrent && (
          <div
            role="alert"
            style={{
              padding: "0.75rem 1rem",
              backgroundColor: "rgba(239, 68, 68, 0.1)",
              border: "1px solid rgba(239, 68, 68, 0.3)",
              borderRadius: "6px",
              marginBottom: "1rem",
              color: "#fca5a5",
              fontSize: "0.875rem",
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>🔒</span>
            <span>You have ReadOnly permissions. Torrents cannot be deleted.</span>
          </div>
        )}

        <div
          id="delete-torrent-modal-desc"
          style={{
            padding: "0.75rem 1rem",
            backgroundColor: "var(--danger-bg-alert, rgba(239, 68, 68, 0.12))",
            border: "1px solid var(--danger-border-alert, rgba(239, 68, 68, 0.3))",
            borderRadius: "6px",
            marginBottom: "1.25rem",
            fontSize: "0.875rem",
            color: "var(--text-primary, #fff)",
            lineHeight: 1.5,
          }}
        >
          {isMultiple ? (
            <span>
              {t(
                "torrents.deleteMultipleConfirm",
                { count },
                `Are you sure you want to delete ${count} torrents?`,
              )}
            </span>
          ) : torrentName ? (
            <span>
              {t(
                "torrents.deleteSingleConfirm",
                undefined,
                "Are you sure you want to delete",
              )}{" "}
              <strong style={{ wordBreak: "break-all" }}>
                "{torrentName}"
              </strong>
              ?
            </span>
          ) : (
            <span>
              {t(
                "torrents.deleteSingleConfirmGeneric",
                undefined,
                "Are you sure you want to delete this torrent?",
              )}
            </span>
          )}
        </div>

        <div style={{ marginBottom: "1.25rem" }}>
          <label
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.6rem",
              cursor: isPending ? "not-allowed" : "pointer",
              userSelect: "none",
              fontSize: "0.9rem",
              color: "var(--text-primary, #fff)",
            }}
          >
            <input
              type="checkbox"
              checked={deleteFiles}
              onChange={(e) => setDeleteFiles(e.target.checked)}
              disabled={isPending}
              style={{
                cursor: isPending ? "not-allowed" : "pointer",
                width: "1.1rem",
                height: "1.1rem",
                accentColor: "var(--danger, #ef4444)",
              }}
            />
            <span>
              {t(
                "torrents.deleteFilesFromDisk",
                undefined,
                "Also delete downloaded files from disk",
              )}
            </span>
          </label>
          {deleteFiles && (
            <p
              style={{
                margin: "0.4rem 0 0 1.7rem",
                fontSize: "0.8rem",
                color: "var(--danger, #ef4444)",
              }}
            >
              {t(
                "torrents.deleteFilesWarning",
                undefined,
                "All downloaded files associated with this torrent will be permanently deleted.",
              )}
            </p>
          )}
        </div>

        <div className="modal-actions">
          <button
            type="button"
            className="btn btn-outline"
            onClick={onClose}
            disabled={isPending}
          >
            {t("common.cancel", undefined, "Cancel")}
          </button>
          <button
            ref={confirmButtonRef}
            type="button"
            className="btn btn-danger"
            onClick={handleConfirm}
            disabled={isPending || !canDeleteTorrent}
            title={
              !canDeleteTorrent
                ? "Deleting torrents requires operator or admin role"
                : undefined
            }
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            {isPending ? (
              <>
                <span
                  className="spinner"
                  style={{
                    display: "inline-block",
                    width: "0.85rem",
                    height: "0.85rem",
                    border: "2px solid rgba(255,255,255,0.3)",
                    borderTopColor: "#fff",
                    borderRadius: "50%",
                    animation: "spin 0.6s linear infinite",
                  }}
                />
                <span>{t("common.deleting", undefined, "Deleting...")}</span>
              </>
            ) : (
              <span>{t("common.delete", undefined, "Delete")}</span>
            )}
          </button>
        </div>
      </div>
    </div>
  );
}

export default DeleteTorrentModal;
