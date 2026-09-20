import { useState, useCallback, useMemo } from "react";
import { Torrent } from "../../api/types";
import { useTorrentFiles, useRenameTorrentFile } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";
import { SkeletonLine } from "../../components/Skeleton";
import { MediaPlayerModal } from "../../components/MediaPlayerModal";
import { isPlayableFile, isAudioFile } from "../../utils/mediaPlayer";
import {
  FileTreeNode,
  FilePriority,
  buildFileTree,
  getDescendantFileIds,
  getDirectoryPriority,
  getDirectoryWanted,
  formatPriority,
} from "../../utils/fileTree";

function FileTreeRow({
  node,
  depth,
  expanded,
  onToggle,
  onDirectoryPriority,
  onFilePriority,
  onDirectoryWanted,
  onFileWanted,
  filePriorities,
  fileWanted,
  onPlayMedia,
  editingPath,
  editingValue,
  onStartRename,
  onSaveRename,
  onCancelRename,
  onChangeRenameValue,
  isRenaming,
}: {
  node: FileTreeNode;
  depth: number;
  expanded: Set<string>;
  onToggle: (path: string) => void;
  onDirectoryPriority: (node: FileTreeNode, priority: FilePriority) => void;
  onFilePriority: (fileId: number, priority: FilePriority) => void;
  onDirectoryWanted: (node: FileTreeNode, wanted: boolean) => void;
  onFileWanted: (fileId: number, wanted: boolean) => void;
  filePriorities: Record<number, FilePriority>;
  fileWanted: Record<number, boolean>;
  onPlayMedia: (file: { id: number; path: string; name: string; size: number }) => void;
  editingPath: string | null;
  editingValue: string;
  onStartRename: (path: string, currentName: string) => void;
  onSaveRename: (oldPath: string, newName: string) => void;
  onCancelRename: () => void;
  onChangeRenameValue: (val: string) => void;
  isRenaming: boolean;
}) {
  const isOpen = expanded.has(node.path);
  const indent = depth * 20;
  const dirPriority = node.isDir
    ? getDirectoryPriority(node, filePriorities)
    : "Normal";
  const filePrio =
    !node.isDir && node.fileId !== undefined
      ? filePriorities[node.fileId] ?? node.priority ?? "Normal"
      : "Normal";
  const dirWanted = node.isDir
    ? getDirectoryWanted(node, fileWanted, filePriorities)
    : { isWanted: true, isIndeterminate: false };
  const isWanted =
    !node.isDir && node.fileId !== undefined
      ? fileWanted[node.fileId] ?? node.wanted ?? (formatPriority(filePrio) !== "Do Not Download")
      : true;
  const progressValue = node.progress ?? 100;
  const playable = !node.isDir && node.fileId !== undefined && isPlayableFile(node.name);

  return (
    <>
      <tr
        className="torrent-table-row"
        style={{ cursor: node.isDir ? "pointer" : "default" }}
        onClick={() => node.isDir && onToggle(node.path)}
      >
        <td
          onClick={(e) => e.stopPropagation()}
          style={{ width: 60, textAlign: "center" }}
        >
          {node.isDir ? (
            <input
              type="checkbox"
              aria-label={`Wanted for folder ${node.name}`}
              checked={dirWanted.isWanted}
              ref={(el) => {
                if (el) {
                  el.indeterminate = dirWanted.isIndeterminate;
                }
              }}
              onChange={(e) => onDirectoryWanted(node, e.target.checked)}
              style={{ cursor: "pointer" }}
            />
          ) : (
            <input
              type="checkbox"
              aria-label={`Wanted for file ${node.name}`}
              checked={isWanted}
              onChange={(e) =>
                node.fileId !== undefined &&
                onFileWanted(node.fileId, e.target.checked)
              }
              style={{ cursor: "pointer" }}
            />
          )}
        </td>
        <td className="mono" style={{ paddingLeft: indent + 8 }}>
          {node.isDir ? (
            <span
              style={{ display: "inline-flex", alignItems: "center", gap: 4 }}
            >
              <span style={{ fontSize: 10, width: 12, textAlign: "center" }}>
                {isOpen ? "▼" : "▶"}
              </span>
              <span style={{ opacity: 0.7 }}>📁</span> {node.name}/
            </span>
          ) : editingPath === node.path ? (
            <div
              onClick={(e) => e.stopPropagation()}
              style={{ display: "inline-flex", alignItems: "center", gap: 4 }}
            >
              <span style={{ width: 12 }} />
              <span style={{ opacity: 0.7 }}>
                {playable ? (isAudioFile(node.name) ? "🎵" : "🎬") : "📄"}
              </span>
              <input
                type="text"
                className="form-control"
                aria-label={`Rename ${node.name}`}
                value={editingValue}
                onChange={(e) => onChangeRenameValue(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    onSaveRename(node.path, editingValue);
                  } else if (e.key === "Escape") {
                    onCancelRename();
                  }
                }}
                disabled={isRenaming}
                autoFocus
                style={{
                  fontSize: "0.8rem",
                  padding: "0.15rem 0.35rem",
                  height: "auto",
                  minWidth: 160,
                }}
              />
              <button
                type="button"
                className="btn btn-xs btn-primary"
                aria-label="Save rename"
                title="Save rename"
                disabled={isRenaming || !editingValue.trim()}
                onClick={() => onSaveRename(node.path, editingValue)}
                style={{ padding: "0.15rem 0.45rem", fontSize: "0.75rem", cursor: "pointer" }}
              >
                {isRenaming ? "..." : "✓"}
              </button>
              <button
                type="button"
                className="btn btn-xs btn-default"
                aria-label="Cancel rename"
                title="Cancel rename"
                disabled={isRenaming}
                onClick={onCancelRename}
                style={{ padding: "0.15rem 0.45rem", fontSize: "0.75rem", cursor: "pointer" }}
              >
                ✕
              </button>
            </div>
          ) : (
            <span
              style={{ display: "inline-flex", alignItems: "center", gap: 4 }}
            >
              <span style={{ width: 12 }} />
              <span style={{ opacity: 0.7 }}>
                {playable ? (isAudioFile(node.name) ? "🎵" : "🎬") : "📄"}
              </span>{" "}
              {playable ? (
                <button
                  type="button"
                  className="btn-link"
                  aria-label={`Play ${node.name}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    onPlayMedia({
                      id: node.fileId!,
                      path: node.path,
                      name: node.name,
                      size: node.size,
                    });
                  }}
                  style={{
                    background: "none",
                    border: "none",
                    padding: 0,
                    color: "var(--accent-color, #3498db)",
                    textDecoration: "underline",
                    cursor: "pointer",
                    fontFamily: "inherit",
                    fontSize: "inherit",
                    textAlign: "left",
                  }}
                >
                  {node.name}
                </button>
              ) : (
                node.name
              )}
            </span>
          )}
        </td>
        <td>{formatBytes(node.size)}</td>
        <td>
          {!node.isDir ? (
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
                    width: `${progressValue}%`,
                    height: "100%",
                    backgroundColor: progressValue >= 100 ? "#2ecc71" : "#3498db",
                  }}
                />
              </div>
              <span style={{ fontSize: "0.75rem", minWidth: 38 }}>
                {progressValue.toFixed(1)}%
              </span>
            </div>
          ) : (
            <span style={{ fontSize: "0.75rem", color: "var(--text-muted, #888)" }}>
              —
            </span>
          )}
        </td>
        <td>
          <div
            onClick={(e) => e.stopPropagation()}
            style={{ display: "inline-flex", alignItems: "center", gap: 4 }}
          >
            {node.isDir ? (
              <select
                aria-label={`Priority for folder ${node.name}`}
                className="form-control"
                style={{
                  fontSize: "0.75rem",
                  padding: "0.15rem 0.35rem",
                  height: "auto",
                  width: "auto",
                  cursor: "pointer",
                }}
                value={dirPriority}
                onChange={(e) =>
                  onDirectoryPriority(node, e.target.value as FilePriority)
                }
              >
                <option value="Normal">Normal</option>
                <option value="High">High</option>
                <option value="Do Not Download">Skip All</option>
                {dirPriority === "Mixed" && (
                  <option value="Mixed" disabled>
                    Mixed
                  </option>
                )}
              </select>
            ) : (
              <select
                aria-label={`Priority for file ${node.name}`}
                className="form-control"
                style={{
                  fontSize: "0.75rem",
                  padding: "0.15rem 0.35rem",
                  height: "auto",
                  width: "auto",
                  cursor: "pointer",
                }}
                value={formatPriority(filePrio)}
                onChange={(e) =>
                  node.fileId !== undefined &&
                  onFilePriority(node.fileId, e.target.value as FilePriority)
                }
              >
                <option value="Normal">Normal</option>
                <option value="High">High</option>
                <option value="Do Not Download">Do Not Download</option>
              </select>
            )}
          </div>
        </td>
        <td style={{ textAlign: "center" }} onClick={(e) => e.stopPropagation()}>
          <div style={{ display: "inline-flex", alignItems: "center", gap: 4, justifyContent: "center" }}>
            {playable && (
              <button
                type="button"
                className="btn btn-xs btn-primary"
                aria-label={`Stream ${node.name}`}
                title={`Stream / Play ${node.name}`}
                onClick={() =>
                  onPlayMedia({
                    id: node.fileId!,
                    path: node.path,
                    name: node.name,
                    size: node.size,
                  })
                }
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "0.25rem",
                  padding: "0.15rem 0.45rem",
                  fontSize: "0.75rem",
                  cursor: "pointer",
                }}
              >
                ▶ Play
              </button>
            )}
            {!node.isDir && node.fileId !== undefined && (
              <button
                type="button"
                className="btn btn-xs btn-default"
                aria-label={`Rename ${node.name}`}
                title={`Rename ${node.name}`}
                onClick={() => onStartRename(node.path, node.name)}
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "0.25rem",
                  padding: "0.15rem 0.45rem",
                  fontSize: "0.75rem",
                  cursor: "pointer",
                }}
              >
                ✏ Rename
              </button>
            )}
            {node.isDir && (
              <span style={{ fontSize: "0.75rem", color: "var(--text-muted, #888)" }}>—</span>
            )}
          </div>
        </td>
      </tr>
      {node.isDir &&
        isOpen &&
        node.children
          .sort((a, b) =>
            a.isDir === b.isDir
              ? a.name.localeCompare(b.name)
              : a.isDir
                ? -1
                : 1,
          )
          .map((child) => (
            <FileTreeRow
              key={child.path}
              node={child}
              depth={depth + 1}
              expanded={expanded}
              onToggle={onToggle}
              onDirectoryPriority={onDirectoryPriority}
              onFilePriority={onFilePriority}
              onDirectoryWanted={onDirectoryWanted}
              onFileWanted={onFileWanted}
              filePriorities={filePriorities}
              fileWanted={fileWanted}
              onPlayMedia={onPlayMedia}
              editingPath={editingPath}
              editingValue={editingValue}
              onStartRename={onStartRename}
              onSaveRename={onSaveRename}
              onCancelRename={onCancelRename}
              onChangeRenameValue={onChangeRenameValue}
              isRenaming={isRenaming}
            />
          ))}
    </>
  );
}

export function FilesTab({ torrent }: { torrent: Torrent }) {
  const { data: files, isLoading, error } = useTorrentFiles(torrent.id);
  const renameFile = useRenameTorrentFile();
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [filePriorities, setFilePriorities] = useState<
    Record<number, FilePriority>
  >({});
  const [fileWanted, setFileWanted] = useState<Record<number, boolean>>({});
  const [editingPath, setEditingPath] = useState<string | null>(null);
  const [editingValue, setEditingValue] = useState<string>("");
  const [renameError, setRenameError] = useState<string | null>(null);
  const [selectedMediaFile, setSelectedMediaFile] = useState<{
    id: number;
    path: string;
    name: string;
    size: number;
  } | null>(null);

  const handleStartRename = useCallback((path: string, currentName: string) => {
    setEditingPath(path);
    setEditingValue(currentName);
    setRenameError(null);
  }, []);

  const handleCancelRename = useCallback(() => {
    setEditingPath(null);
    setEditingValue("");
    setRenameError(null);
  }, []);

  const handleSaveRename = useCallback(
    (oldPath: string, newName: string) => {
      const trimmed = newName.trim();
      if (!trimmed) return;

      let newPath: string;
      const normalizedOld = oldPath.replace(/\\/g, "/");
      const lastSlash = normalizedOld.lastIndexOf("/");
      if (trimmed.includes("/")) {
        newPath = trimmed;
      } else if (lastSlash >= 0) {
        newPath = `${normalizedOld.substring(0, lastSlash)}/${trimmed}`;
      } else {
        newPath = trimmed;
      }

      renameFile.mutate(
        {
          hash: torrent.infoHash,
          oldPath,
          newPath,
          torrentId: torrent.id,
        },
        {
          onSuccess: () => {
            setEditingPath(null);
            setEditingValue("");
            setRenameError(null);
          },
          onError: (err: any) => {
            setRenameError(err?.message || "Failed to rename file");
          },
        },
      );
    },
    [renameFile, torrent.infoHash, torrent.id],
  );

  const toggleDir = useCallback((path: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }, []);

  const handleDirectoryPriority = useCallback(
    (dirNode: FileTreeNode, priority: FilePriority) => {
      const ids = getDescendantFileIds(dirNode);
      setFilePriorities((prev) => {
        const next = { ...prev };
        for (const id of ids) {
          next[id] = priority;
        }
        return next;
      });
      setFileWanted((prev) => {
        const next = { ...prev };
        const isWanted = priority !== "Do Not Download" && priority !== "Skip All";
        for (const id of ids) {
          next[id] = isWanted;
        }
        return next;
      });
    },
    [],
  );

  const handleFilePriority = useCallback(
    (fileId: number, priority: FilePriority) => {
      setFilePriorities((prev) => ({
        ...prev,
        [fileId]: priority,
      }));
      setFileWanted((prev) => ({
        ...prev,
        [fileId]: formatPriority(priority) !== "Do Not Download",
      }));
    },
    [],
  );

  const handleDirectoryWanted = useCallback(
    (dirNode: FileTreeNode, wanted: boolean) => {
      const ids = getDescendantFileIds(dirNode);
      setFileWanted((prev) => {
        const next = { ...prev };
        for (const id of ids) {
          next[id] = wanted;
        }
        return next;
      });
      setFilePriorities((prev) => {
        const next = { ...prev };
        for (const id of ids) {
          next[id] = wanted ? "Normal" : "Do Not Download";
        }
        return next;
      });
    },
    [],
  );

  const handleFileWanted = useCallback((fileId: number, wanted: boolean) => {
    setFileWanted((prev) => ({
      ...prev,
      [fileId]: wanted,
    }));
    setFilePriorities((prev) => ({
      ...prev,
      [fileId]: wanted ? "Normal" : "Do Not Download",
    }));
  }, []);

  const nonPaddingFiles = useMemo(
    () => files?.filter((f) => !f.isPaddingFile) ?? [],
    [files],
  );

  function expandAll() {
    if (!nonPaddingFiles.length) return;
    const dirs = new Set<string>();
    for (const f of nonPaddingFiles) {
      const normalized = f.path.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
      const parts = normalized.split("/").filter(Boolean);
      for (let i = 1; i < parts.length; i++) {
        dirs.add(parts.slice(0, i).join("/"));
      }
    }
    setExpanded(dirs);
  }

  const torrentProgress =
    torrent.status === "Seeding" || (torrent.progress ?? 0) >= 1.0
      ? 100
      : (torrent.progress ?? 0) * 100;

  const tree = nonPaddingFiles.length > 0
    ? buildFileTree(nonPaddingFiles, {
        priorities: filePriorities,
        wanted: fileWanted,
        progress: torrentProgress,
      })
    : [];
  const hasDirectories = tree.some((n) => n.isDir);

  return (
    <div className="card">
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
        }}
      >
        <h3>Files ({nonPaddingFiles.length})</h3>
        {hasDirectories && (
          <div style={{ display: "flex", gap: 4 }}>
            <button className="btn btn-sm btn-default" onClick={expandAll}>
              Expand All
            </button>
            <button
              className="btn btn-sm btn-default"
              onClick={() => setExpanded(new Set())}
            >
              Collapse All
            </button>
          </div>
        )}
      </div>
      {isLoading && (
        <div className="torrent-table-wrapper">
          <SkeletonLine width="100%" height="2rem" />
          <SkeletonLine width="100%" height="1.5rem" />
          <SkeletonLine width="100%" height="1.5rem" />
        </div>
      )}
      {error && <p className="error">Failed to load files.</p>}
      {files && files.length === 0 && (
        <p className="torrent-table-empty">No files found</p>
      )}
      {tree.length > 0 && (
        <div className="torrent-table-wrapper">
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th" style={{ width: 60, textAlign: "center" }}>
                  Wanted
                </th>
                <th className="torrent-table-th">Path</th>
                <th className="torrent-table-th" style={{ width: 100 }}>
                  Size
                </th>
                <th className="torrent-table-th" style={{ width: 130 }}>
                  Progress
                </th>
                <th className="torrent-table-th" style={{ width: 150 }}>
                  Priority
                </th>
                <th className="torrent-table-th" style={{ width: 140, textAlign: "center" }}>
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {tree
                .sort((a, b) =>
                  a.isDir === b.isDir
                    ? a.name.localeCompare(b.name)
                    : a.isDir
                      ? -1
                      : 1,
                )
                .map((node) => (
                  <FileTreeRow
                    key={node.path}
                    node={node}
                    depth={0}
                    expanded={expanded}
                    onToggle={toggleDir}
                    onDirectoryPriority={handleDirectoryPriority}
                    onFilePriority={handleFilePriority}
                    onDirectoryWanted={handleDirectoryWanted}
                    onFileWanted={handleFileWanted}
                    filePriorities={filePriorities}
                    fileWanted={fileWanted}
                    onPlayMedia={setSelectedMediaFile}
                    editingPath={editingPath}
                    editingValue={editingValue}
                    onStartRename={handleStartRename}
                    onSaveRename={handleSaveRename}
                    onCancelRename={handleCancelRename}
                    onChangeRenameValue={setEditingValue}
                    isRenaming={renameFile.isPending}
                  />
                ))}
            </tbody>
          </table>
        </div>
      )}
      {renameError && (
        <p className="error" style={{ marginTop: "0.5rem" }}>
          {renameError}
        </p>
      )}
      {selectedMediaFile && (
        <MediaPlayerModal
          isOpen={Boolean(selectedMediaFile)}
          onClose={() => setSelectedMediaFile(null)}
          torrent={torrent}
          file={selectedMediaFile}
        />
      )}
    </div>
  );
}
