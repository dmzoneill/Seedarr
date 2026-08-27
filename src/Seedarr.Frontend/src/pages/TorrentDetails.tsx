import { useState } from "react";
import { useParams, Link } from "react-router";
import { useTorrent, useStartSeeding, useStopSeeding } from "../api/hooks";
import PeerList from "../components/PeerList";
import { GeneralTab } from "./torrentdetails/GeneralTab";
import { FilesTab } from "./torrentdetails/FilesTab";
import { TrackersTab } from "./torrentdetails/TrackersTab";
import { OptionsTab } from "./torrentdetails/OptionsTab";
import { MonitoringTab } from "./torrentdetails/MonitoringTab";
import { LogTab } from "./torrentdetails/LogTab";
import { TorrentDetailSkeleton } from "./torrentdetails/shared";

type Tab =
  "general" | "files" | "trackers" | "options" | "peers" | "monitoring" | "log";

const tabs: { key: Tab; label: string }[] = [
  { key: "general", label: "General" },
  { key: "files", label: "Files" },
  { key: "trackers", label: "Trackers" },
  { key: "options", label: "Options" },
  { key: "peers", label: "Peers" },
  { key: "monitoring", label: "Monitoring" },
  { key: "log", label: "Seeder Log" },
];

function TorrentDetails() {
  const { id } = useParams<{ id: string }>();
  const parsed = Number(id);
  const isValidId = id !== undefined && !isNaN(parsed) && parsed > 0;
  const torrentId = isValidId ? parsed : 0;
  const { data: torrent, isLoading, error } = useTorrent(torrentId);
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const [activeTab, setActiveTab] = useState<Tab>("general");

  if (!isValidId) {
    return (
      <div className="content-area">
        <Link to="/torrents" className="back-link">
          ← Back to Torrents
        </Link>
        <p className="error">Invalid torrent ID.</p>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="content-area">
        <Link to="/torrents" className="back-link">
          ← Back to Torrents
        </Link>
        <TorrentDetailSkeleton />
      </div>
    );
  }

  if (error || !torrent) {
    return (
      <div className="content-area">
        <Link to="/torrents" className="back-link">
          ← Back to Torrents
        </Link>
        <p className="error">Torrent not found.</p>
      </div>
    );
  }

  const isSeeding = torrent.status === "Seeding";

  return (
    <div
      className="content-area"
      style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
    >
      {/* Top Breadcrumb & Actions Bar */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <Link
            to="/torrents"
            className="back-link"
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.35rem",
              padding: 0,
              margin: "0 0 0.4rem 0",
            }}
          >
            ← Back to Torrents
          </Link>
          <h1 style={{ margin: 0, fontSize: "1.25rem", fontWeight: 600 }}>
            {torrent.name}
          </h1>
        </div>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          {isSeeding ? (
            <button
              className="btn btn-danger"
              onClick={() => stopSeeding.mutate(torrent.id)}
            >
              Stop Seeding
            </button>
          ) : (
            <button
              className="btn btn-success"
              onClick={() => startSeeding.mutate(torrent.id)}
            >
              Start Seeding
            </button>
          )}
        </div>
      </div>

      {/* Tab Navigation */}
      <nav
        className="tab-nav"
        style={{
          borderRadius: "6px",
          border: "1px solid var(--border-light)",
          padding: "0 0.5rem",
          margin: 0,
        }}
      >
        {tabs.map((tab) => (
          <button
            key={tab.key}
            className={`tab-btn${activeTab === tab.key ? " tab-btn-active" : ""}`}
            onClick={() => setActiveTab(tab.key)}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      {/* Active Tab Content Pane */}
      <div style={{ flex: 1 }}>
        {activeTab === "general" && <GeneralTab torrent={torrent} />}
        {activeTab === "files" && <FilesTab torrent={torrent} />}
        {activeTab === "trackers" && <TrackersTab torrent={torrent} />}
        {activeTab === "options" && <OptionsTab torrent={torrent} />}
        {activeTab === "peers" && <PeerList torrentId={torrent.id} />}
        {activeTab === "monitoring" && <MonitoringTab torrent={torrent} />}
        {activeTab === "log" && <LogTab torrent={torrent} />}
      </div>
    </div>
  );
}

export default TorrentDetails;
