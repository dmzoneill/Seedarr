import { useEffect } from "react";
import { useTranslation } from "../i18n";
import AddTorrentForm, { InputMode } from "./AddTorrentForm";
import { useModalRegistration } from "./ModalProvider";
import { useFocusTrap } from "../hooks/useFocusTrap";
import { usePermissions } from "../hooks/usePermissions";
import { trackModalOpen } from "../utils/analytics";

interface AddTorrentModalProps {
  initialMode?: InputMode;
  initialQuery?: string;
  onClose: () => void;
}

function AddTorrentModal({
  initialMode = "file",
  initialQuery = "",
  onClose,
}: AddTorrentModalProps) {
  const { t } = useTranslation();
  const { canAddTorrent } = usePermissions();

  useEffect(() => {
    trackModalOpen("add_torrent");
  }, []);

  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen: true,
    onEscape: onClose,
    onClose,
  });

  useModalRegistration({
    id: "add-torrent-modal",
    isOpen: true,
    onClose,
    modalRef: trapRef,
  });

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  return (
    <div
      className="modal-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="add-torrent-modal-title"
    >
      <div
        ref={trapRef}
        className="modal"
        style={{
          maxWidth: "1020px",
          width: "92%",
          borderRadius: "8px",
          padding: "1.5rem",
          boxShadow:
            "0 12px 40px rgba(0, 0, 0, 0.6), 0 2px 8px rgba(0, 0, 0, 0.3)",
          border: "1px solid var(--border-light)",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1.25rem",
          }}
        >
          <h2
            id="add-torrent-modal-title"
            className="modal-title"
            style={{ margin: 0, fontSize: "1.2rem" }}
          >
            {t("modals.addTorrent.title", "Add Torrent")}
          </h2>
          <button
            type="button"
            className="btn btn-outline"
            style={{
              padding: "0.2rem 0.5rem",
              fontSize: "0.85rem",
              borderRadius: "4px",
            }}
            onClick={onClose}
            title={t("common.close", "Close dialog")}
            aria-label={t(
              "modals.addTorrent.closeDialog",
              "Close add torrent dialog",
            )}
          >
            ✕
          </button>
        </div>

        {!canAddTorrent ? (
          <div style={{ padding: "1rem 0" }}>
            <div
              role="alert"
              style={{
                padding: "0.75rem 1rem",
                backgroundColor: "rgba(239, 68, 68, 0.1)",
                border: "1px solid rgba(239, 68, 68, 0.3)",
                borderRadius: "6px",
                color: "#fca5a5",
                fontSize: "0.875rem",
              }}
            >
              {t(
                "modals.addTorrent.readOnlyWarning",
                "🔒 You have ReadOnly permissions. Adding torrents is not permitted.",
              )}
            </div>
            <div
              style={{
                marginTop: "1rem",
                display: "flex",
                justifyContent: "flex-end",
              }}
            >
              <button className="btn btn-outline" onClick={onClose}>
                {t("common.close", "Close")}
              </button>
            </div>
          </div>
        ) : (
          <AddTorrentForm
            initialMode={initialMode}
            initialQuery={initialQuery}
            isModal={true}
            onClose={onClose}
          />
        )}
      </div>
    </div>
  );
}

export default AddTorrentModal;
