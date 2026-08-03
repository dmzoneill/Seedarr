import { useState, useCallback } from "react";
import { Torrent, TorrentFileInfo } from "../../api/types";
import { useTorrentFiles } from "../../api/hooks";
import { formatBytes } from "../../utils/formatters";
import { SkeletonLine } from "../../components/Skeleton";

interface FileTreeNode {
  name: string;
  path: string;
  size: number;
  isDir: boolean;
  children: FileTreeNode[];
  fileId?: number;
}

function buildFileTree(files: TorrentFileInfo[]): FileTreeNode[] {
  const root: FileTreeNode[] = [];

  for (const file of files) {
    const parts = file.path.split("/");
    let current = root;

    for (let i = 0; i < parts.length; i++) {
      const part = parts[i];
      const isLast = i === parts.length - 1;
      let existing = current.find(
        (n) => n.name === part && n.isDir === !isLast,
      );

      if (!existing) {
        existing = {
          name: part,
          path: parts.slice(0, i + 1).join("/"),
          size: isLast ? file.size : 0,
          isDir: !isLast,
          children: [],
          fileId: isLast ? file.id : undefined,
        };
        current.push(existing);
      }

      if (!isLast) {
        existing.size += file.size;
        current = existing.children;
      }
    }
  }

  return root;
}

function FileTreeRow({
  node,
  depth,
  expanded,
  onToggle,
}: {
  node: FileTreeNode;
  depth: number;
  expanded: Set<string>;
  onToggle: (path: string) => void;
}) {
  const isOpen = expanded.has(node.path);
  const indent = depth * 20;

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
            />
          ))}
    </>
  );
}

export function FilesTab({ torrent }: { torrent: Torrent }) {
  const { data: files, isLoading, error } = useTorrentFiles(torrent.id);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const toggleDir = useCallback((path: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }, []);

  function expandAll() {
    if (!files) return;
    const dirs = new Set<string>();
    for (const f of files) {
      const parts = f.path.split("/");
      for (let i = 1; i < parts.length; i++) {
        dirs.add(parts.slice(0, i).join("/"));
      }
    }
    setExpanded(dirs);
  }

  const tree = files ? buildFileTree(files) : [];
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
        <h3>Files ({files?.length ?? 0})</h3>
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
                <th className="torrent-table-th">Size</th>
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
                  />
                ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
