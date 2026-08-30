import AddTorrentForm, { InputMode } from "./AddTorrentForm";

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
  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  return (
    <div className="modal-overlay" onClick={handleBackdropClick}>
      <div
        className="modal"
        style={{
          maxWidth: "1020px",
          width: "92%",
          borderRadius: "8px",
          padding: "1.5rem",
          boxShadow:
            "0 12px 40px rgba(0, 0, 0, 0.6), 0 2px 8px rgba(0, 0, 0, 0.3)",
          border: "1px solid rgba(255, 255, 255, 0.1)",
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
          <h2 className="modal-title" style={{ margin: 0, fontSize: "1.2rem" }}>
            Add Torrent
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
            title="Close dialog"
          >
            ✕
          </button>
        </div>

        <AddTorrentForm
          initialMode={initialMode}
          initialQuery={initialQuery}
          isModal={true}
          onClose={onClose}
        />
      </div>
    </div>
  );
}

export default AddTorrentModal;
