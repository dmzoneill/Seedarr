import { useNavigate } from "react-router";
import AddTorrentForm from "../components/AddTorrentForm";
import { usePermissions } from "../hooks/usePermissions";

export function AddTorrentPage() {
  const navigate = useNavigate();
  const { canAddTorrent } = usePermissions();

  if (!canAddTorrent) {
    return (
      <div className="content-area" style={{ padding: "1.5rem" }}>
        <div
          role="alert"
          style={{
            padding: "0.75rem 1rem",
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
          <span>
            You have ReadOnly permissions. Adding torrents is not permitted.
          </span>
        </div>
      </div>
    );
  }

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
      }}
    >
      <div
        className="page-header"
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
            <span>➕</span> Add Torrent
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Add a new torrent via file upload or magnet link
          </p>
        </div>
      </div>

      <div
        className="card"
        style={{
          borderRadius: "8px",
          padding: "1.5rem",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          flex: "1 1 auto",
          display: "flex",
          flexDirection: "column",
          minHeight: 0,
          overflow: "hidden",
          width: "100%",
        }}
      >
        <AddTorrentForm
          isModal={false}
          onSuccess={() => navigate("/torrents")}
        />
      </div>
    </div>
  );
}

export default AddTorrentPage;
