import { useState, useCallback, useMemo } from "react";
import { Torrent } from "../../api/types";
import { useTorrentFiles } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";
import { SkeletonLine } from "../../components/Skeleton";
import {
  FileTreeNode,
  FilePriority,
  buildFileTree,
  getDescendantFileIds,
  getDirectoryPriority,
  formatPriority,
} from "../../utils/fileTree";

function FileTreeRow({
  node,
  depth,
  expanded,
  onToggle,
  onDirectoryPriority,
  onFilePriority,
  filePriorities,
}: {
  node: FileTreeNode;
  depth: number;
  expanded: Set<string>;
  onToggle: (path: string) => void;
  onDirectoryPriority: (node: FileTreeNode, priority: FilePriority) => void;
  onFilePriority: (fileId: number, priority: FilePriority) => void;
  filePriorities: Record<number, FilePriority>;
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
  const progressValue = node.progress ?? 100;

  return (
    <>
      <tr
        className="torrent-table-row"
        style={{ cursor: node.isDir ? "pointer" : "default" }}
        onClick={() => node.isDir && onToggle(node.path)}
      >
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
          ) : (
            <span
              style={{ display: "inline-flex", alignItems: "center", gap: 4 }}
            >
              <span style={{ width: 12 }} />
              <span style={{ opacity: 0.7 }}>📄</span> {node.name}
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
              filePriorities={filePriorities}
            />
          ))}
    </>
  );
}

export function FilesTab({ torrent }: { torrent: Torrent }) {
  const { data: files, isLoading, error } = useTorrentFiles(torrent.id);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [filePriorities, setFilePriorities] = useState<
    Record<number, FilePriority>
  >({});

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
    },
    [],
  );

  const handleFilePriority = useCallback(
    (fileId: number, priority: FilePriority) => {
      setFilePriorities((prev) => ({
        ...prev,
        [fileId]: priority,
      }));
    },
    [],
  );

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
                    filePriorities={filePriorities}
                  />
                ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
