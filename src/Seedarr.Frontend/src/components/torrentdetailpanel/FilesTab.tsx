import { useState, useCallback } from "react";
import { useTorrentFiles } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";
import { FilePriority, formatPriority } from "../../utils/fileTree";
import { trackFilePriorityChange } from "../../utils/analytics";

export function FilesTab({ torrentId }: { torrentId: number }) {
  const { data: files, isLoading, isError } = useTorrentFiles(torrentId);
  const [filePriorities, setFilePriorities] = useState<Record<number, FilePriority>>({});
  const [fileWanted, setFileWanted] = useState<Record<number, boolean>>({});

  const handleWantedChange = useCallback((fileId: number, checked: boolean) => {
    trackFilePriorityChange(checked ? "normal" : "skip", 1);
    setFileWanted((prev) => ({ ...prev, [fileId]: checked }));
    setFilePriorities((prev) => ({
      ...prev,
      [fileId]: checked ? "Normal" : "Do Not Download",
    }));
  }, []);

  const handlePriorityChange = useCallback((fileId: number, priority: FilePriority) => {
    const isSkip = formatPriority(priority) === "Do Not Download";
    trackFilePriorityChange(isSkip ? "skip" : priority === "High" ? "high" : "normal", 1);
    setFilePriorities((prev) => ({ ...prev, [fileId]: priority }));
    setFileWanted((prev) => ({
      ...prev,
      [fileId]: !isSkip,
    }));
  }, []);

  const nonPaddingFiles = files?.filter((f) => !f.isPaddingFile) ?? [];

  if (isLoading) return <PanelLoading>Loading files...</PanelLoading>;
  if (isError) return <PanelEmpty>Failed to load files.</PanelEmpty>;
  if (nonPaddingFiles.length === 0) return <PanelEmpty>No files</PanelEmpty>;

  return (
    <div className="detail-panel-table-wrap">
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th" style={{ width: 60, textAlign: "center" }}>Wanted</th>
            <th className="torrent-table-th">Path</th>
            <th className="torrent-table-th" style={{ width: 100 }}>Size</th>
            <th className="torrent-table-th" style={{ width: 130 }}>Progress</th>
            <th className="torrent-table-th" style={{ width: 150 }}>Priority</th>
          </tr>
        </thead>
        <tbody>
          {nonPaddingFiles.map((f) => {
            const prio = filePriorities[f.id] ?? (f.wanted === false || f.priority === 0 ? "Do Not Download" : (f.priority === 6 || f.priority === 7 || f.priority === 2 ? "High" : "Normal"));
            const wanted = fileWanted[f.id] ?? (f.wanted !== undefined ? f.wanted : formatPriority(prio) !== "Do Not Download");
            const progress = f.bytesCompleted !== undefined && f.size > 0
              ? Math.min(100, Math.max(0, (f.bytesCompleted / f.size) * 100))
              : (wanted ? 100 : 0);

            return (
              <tr key={f.id} className="torrent-table-row">
                <td style={{ textAlign: "center" }}>
                  <input
                    type="checkbox"
                    aria-label={`Wanted for file ${f.path}`}
                    checked={wanted}
                    onChange={(e) => handleWantedChange(f.id, e.target.checked)}
                    style={{ cursor: "pointer" }}
                  />
                </td>
                <td className="mono">{f.path}</td>
                <td>{formatBytes(f.size)}</td>
                <td>
                  <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                    <div
                      style={{
                        flex: 1,
                        minWidth: 40,
                        maxWidth: 70,
                        height: 6,
                        backgroundColor: "rgba(255, 255, 255, 0.1)",
                        borderRadius: 3,
                        overflow: "hidden",
                      }}
                    >
                      <div
                        style={{
                          width: `${progress}%`,
                          height: "100%",
                          backgroundColor: progress >= 100 ? "#2ecc71" : "#3498db",
                        }}
                      />
                    </div>
                    <span style={{ fontSize: "0.75rem", minWidth: 38 }}>
                      {progress.toFixed(1)}%
                    </span>
                  </div>
                </td>
                <td>
                  <select
                    aria-label={`Priority for file ${f.path}`}
                    className="form-control"
                    style={{
                      fontSize: "0.75rem",
                      padding: "0.15rem 0.35rem",
                      height: "auto",
                      width: "auto",
                      cursor: "pointer",
                    }}
                    value={formatPriority(prio)}
                    onChange={(e) => handlePriorityChange(f.id, e.target.value as FilePriority)}
                  >
                    <option value="Normal">Normal</option>
                    <option value="High">High</option>
                    <option value="Do Not Download">Do Not Download</option>
                  </select>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
