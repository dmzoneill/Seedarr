import { useNavigate } from "react-router";
import AddTorrentForm from "../components/AddTorrentForm";

export function AddTorrentPage() {
  const navigate = useNavigate();

  return (
    <div className="content-area">
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
        }}
      >
        <div className="page-header-group">
          <h1 className="page-heading" style={{ margin: 0 }}>
            Add Torrent
          </h1>
        </div>
      </div>

      <div
        className="card"
        style={{
          borderRadius: "8px",
          padding: "1.5rem",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          maxWidth: "1100px",
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
