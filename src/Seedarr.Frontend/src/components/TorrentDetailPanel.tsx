import { useState } from "react";
import { useTorrent, useStartSeeding, useStopSeeding } from "../api/hooks";
import {
  InfoIcon,
  ClipboardIcon,
  FileIcon,
  UsersIcon,
  GlobeIcon,
  SlidersIcon,
  ActivityIcon,
  HashIcon,
} from "./icons/UIIcons";
import { PeerMapIcon } from "./icons/AppIcons";
import { usePanelHeight } from "./torrentdetailpanel/shared";
import { StatusTab } from "./torrentdetailpanel/StatusTab";
import { DetailsTab } from "./torrentdetailpanel/DetailsTab";
import { FilesTab } from "./torrentdetailpanel/FilesTab";
import { PeersTab } from "./torrentdetailpanel/PeersTab";
import { TrackersTab } from "./torrentdetailpanel/TrackersTab";
import { OptionsTab } from "./torrentdetailpanel/OptionsTab";
import { MonitoringTab } from "./torrentdetailpanel/MonitoringTab";
import { LogTab } from "./torrentdetailpanel/LogTab";
import { CliTab } from "./torrentdetailpanel/CliTab";
import PieceMap from "./PieceMap";

type DetailTab =
  | "status"
  | "details"
  | "files"
  | "cli"
  | "peers"
  | "trackers"
  | "options"
  | "piecemap"
  | "monitoring"
  | "log";

interface TorrentDetailPanelProps {
  torrentId: number;
  onClose: () => void;
}

const TAB_ICONS: Record<DetailTab, React.ReactNode> = {
  status: <InfoIcon size={13} />,
  details: <ClipboardIcon size={13} />,
  files: <FileIcon size={13} />,
  cli: (
    <span
      style={{ fontSize: "0.75rem", fontFamily: "monospace", fontWeight: 700 }}
    >
      &gt;_
    </span>
  ),
  peers: <UsersIcon size={13} />,
  trackers: <GlobeIcon size={13} />,
  options: <SlidersIcon size={13} />,
  piecemap: <PeerMapIcon size={13} />,
  monitoring: <ActivityIcon size={13} />,
  log: <HashIcon size={13} />,
};

const DETAIL_TABS: { key: DetailTab; label: string }[] = [
  { key: "status", label: "Status" },
  { key: "details", label: "Details" },
  { key: "files", label: "Files" },
  { key: "cli", label: "Terminal / CLI" },
  { key: "peers", label: "Peers" },
  { key: "trackers", label: "Trackers" },
  { key: "options", label: "Options" },
  { key: "piecemap", label: "Piece Map" },
  { key: "monitoring", label: "Monitoring" },
  { key: "log", label: "Seeder Log" },
];

function TorrentDetailPanel({ torrentId, onClose }: TorrentDetailPanelProps) {
  const { data: torrent, isLoading, isError } = useTorrent(torrentId);
  const startSeeding = useStartSeeding();
  const stopSeeding = useStopSeeding();
  const [tab, setTab] = useState<DetailTab>("status");
  const { height, panelRef, onMouseDown } = usePanelHeight();

  if (isLoading)
    return (
      <div className="detail-panel" style={{ height }}>
        <div className="detail-panel-loading">Loading...</div>
      </div>
    );
  if (isError)
    return (
      <div className="detail-panel" style={{ height }}>
        <div className="detail-panel-empty">Failed to load torrent.</div>
      </div>
    );
  if (!torrent)
    return (
      <div className="detail-panel" style={{ height }}>
        <div className="detail-panel-empty">Torrent not found</div>
      </div>
    );

  const isSeeding = torrent.status === "Seeding";

  return (
    <div className="detail-panel" ref={panelRef} style={{ height }}>
      <div className="detail-panel-resize-handle" onMouseDown={onMouseDown} />
      <div className="detail-panel-header">
        <div className="detail-panel-title">{torrent.name}</div>
        <div className="detail-panel-actions">
          {isSeeding ? (
            <button
              className="btn btn-small btn-danger"
              onClick={() => stopSeeding.mutate(torrent.id)}
            >
              Stop
            </button>
          ) : (
            <button
              className="btn btn-small btn-success"
              onClick={() => startSeeding.mutate(torrent.id)}
            >
              Start
            </button>
          )}
          <button
            className="btn btn-small"
            onClick={onClose}
            title="Close panel"
          >
            X
          </button>
        </div>
      </div>
      <nav className="detail-panel-tabs" role="tablist" aria-label="Torrent Details Tabs">
        {DETAIL_TABS.map((t, idx) => (
          <button
            key={t.key}
            role="tab"
            aria-selected={tab === t.key}
            tabIndex={tab === t.key ? 0 : -1}
            className={`tab-btn${tab === t.key ? " tab-btn-active" : ""}`}
            onClick={() => setTab(t.key)}
            onKeyDown={(e) => {
              if (e.key === "ArrowRight") {
                e.preventDefault();
                const nextIdx = (idx + 1) % DETAIL_TABS.length;
                setTab(DETAIL_TABS[nextIdx].key);
                const nextBtn = e.currentTarget.parentElement?.children[nextIdx] as HTMLElement;
                nextBtn?.focus();
              } else if (e.key === "ArrowLeft") {
                e.preventDefault();
                const prevIdx = (idx - 1 + DETAIL_TABS.length) % DETAIL_TABS.length;
                setTab(DETAIL_TABS[prevIdx].key);
                const prevBtn = e.currentTarget.parentElement?.children[prevIdx] as HTMLElement;
                prevBtn?.focus();
              }
            }}
          >
            {TAB_ICONS[t.key]} {t.label}
          </button>
        ))}
      </nav>
      <div className="detail-panel-body">
        {tab === "status" && <StatusTab torrent={torrent} />}
        {tab === "details" && <DetailsTab torrent={torrent} />}
        {tab === "files" && <FilesTab torrent={torrent} torrentId={torrent.id} />}
        {tab === "cli" && <CliTab torrent={torrent} />}
        {tab === "peers" && <PeersTab torrent={torrent} torrentId={torrent.id} />}
        {tab === "trackers" && <TrackersTab torrentId={torrent.id} />}
        {tab === "options" && <OptionsTab torrent={torrent} />}
        {tab === "piecemap" && (
          <PieceMap
            torrentId={torrent.id}
            pieceCount={torrent.pieceCount}
            pieceLength={torrent.pieceLength}
            progress={torrent.progress}
            isSeeding={isSeeding}
          />
        )}
        {tab === "monitoring" && <MonitoringTab torrent={torrent} />}
        {tab === "log" && <LogTab torrent={torrent} />}
      </div>
    </div>
  );
}

export default TorrentDetailPanel;
