import { Torrent } from "../../api/types";
import {
  formatBytes,
  formatDate,
  formatRatio,
  formatSeconds,
} from "../../utils/formatters";
import { StatusRow } from "./shared";

export function GeneralTab({ torrent }: { torrent: Torrent }) {
  return (
    <div className="detail-grid">
      <div className="card">
        <h3>Info</h3>
        <StatusRow label="Name">{torrent.name}</StatusRow>
        <StatusRow label="Info Hash" mono>
          {torrent.infoHash}
        </StatusRow>
        <StatusRow label="Size">{formatBytes(torrent.totalSize)}</StatusRow>
        <StatusRow label="Pieces">
          {torrent.pieceCount} x {formatBytes(torrent.pieceLength)}
        </StatusRow>
        <StatusRow label="Private">
          {torrent.isPrivate ? "Yes" : "No"}
        </StatusRow>
        <StatusRow label="Created">
          {formatDate(torrent.creationDate)}
        </StatusRow>
        {torrent.createdBy && (
          <StatusRow label="Created By">{torrent.createdBy}</StatusRow>
        )}
        {torrent.comment && (
          <StatusRow label="Comment">{torrent.comment}</StatusRow>
        )}
        {torrent.sourcePath && (
          <StatusRow label="Source Path" mono>
            {torrent.sourcePath}
          </StatusRow>
        )}
      </div>

      <div className="card">
        <h3>Stats</h3>
        <StatusRow label="Status">
          <span className={`badge badge-${torrent.status.toLowerCase()}`}>
            {torrent.status}
          </span>
        </StatusRow>
        <StatusRow label="Uploaded">{formatBytes(torrent.uploaded)}</StatusRow>
        <StatusRow label="Downloaded">
          {formatBytes(torrent.downloaded)}
        </StatusRow>
        <StatusRow label="Ratio">{formatRatio(torrent.ratio)}</StatusRow>
        <StatusRow label="Seeding Time">
          {formatSeconds(torrent.seedingTime)}
        </StatusRow>
        <StatusRow label="Seeders">{torrent.seeders}</StatusRow>
        <StatusRow label="Leechers">{torrent.leechers}</StatusRow>
        <StatusRow label="Added">{formatDate(torrent.dateAdded)}</StatusRow>
        <StatusRow label="Last Active">
          {formatDate(torrent.lastActive)}
        </StatusRow>
      </div>
    </div>
  );
}
