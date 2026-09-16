import type { TorrentFileInfo } from "../api/types";

export type FilePriority =
  | "skip"
  | "normal"
  | "high"
  | "Do Not Download"
  | "Normal"
  | "High"
  | "Skip All"
  | number;

export interface FileTreeNode {
  name: string;
  path: string;
  size: number;
  isDir: boolean;
  children: FileTreeNode[];
  fileId?: number;
  fileIndex?: number;
  priority?: FilePriority;
  progress?: number;
  cascadePriority?: (priority: FilePriority) => void;
}

export interface BuildFileTreeOptions {
  priorities?: Record<number, FilePriority>;
  progress?: Record<number, number> | number;
  defaultPriority?: FilePriority;
}

/**
 * Builds a hierarchical tree from a flat list of torrent files.
 * Normalizes Windows backslashes, mixed slashes, leading/trailing slashes,
 * and consecutive duplicate slashes to prevent ghost directory nodes.
 */
export function buildFileTree(
  files: Array<Partial<TorrentFileInfo> & { path: string; size?: number }>,
  options?: BuildFileTreeOptions,
): FileTreeNode[] {
  const root: FileTreeNode[] = [];
  const defaultPriority: FilePriority = options?.defaultPriority ?? "Normal";

  for (let fileIndex = 0; fileIndex < files.length; fileIndex++) {
    const file = files[fileIndex];
    if (!file || typeof file.path !== "string") continue;

    const normalizedPath = file.path
      .replace(/\\/g, "/")
      .replace(/^\/+|\/+$/g, "");
    const parts = normalizedPath.split("/").filter(Boolean);

    if (parts.length === 0) {
      parts.push("unnamed");
    }

    let current = root;

    for (let i = 0; i < parts.length; i++) {
      const part = parts[i];
      const isLast = i === parts.length - 1;
      let existing = current.find(
        (n) => n.name === part && n.isDir === !isLast,
      );

      if (!existing) {
        const nodePath = parts.slice(0, i + 1).join("/");
        const fileId = isLast ? file.id : undefined;
        const priority = isLast
          ? (fileId !== undefined && options?.priorities?.[fileId] !== undefined
              ? options.priorities[fileId]
              : defaultPriority)
          : defaultPriority;

        let nodeProgress: number | undefined = undefined;
        if (isLast) {
          if (typeof options?.progress === "number") {
            nodeProgress = options.progress;
          } else if (fileId !== undefined && options?.progress?.[fileId] !== undefined) {
            nodeProgress = options.progress[fileId];
          } else {
            nodeProgress = 100;
          }
        }

        existing = {
          name: part,
          path: nodePath,
          size: isLast ? (file.size ?? 0) : 0,
          isDir: !isLast,
          children: [],
          fileId,
          fileIndex: isLast ? fileIndex : undefined,
          priority,
          progress: nodeProgress,
        };

        existing.cascadePriority = function (p: FilePriority) {
          cascadePriority(this, p);
        };

        current.push(existing);
      }

      if (!isLast) {
        existing.size += file.size ?? 0;
        current = existing.children;
      }
    }
  }

  return root;
}

/**
 * Recursively applies priority to a node and all of its descendants.
 */
function applyPriorityRecursive(node: FileTreeNode, priority: FilePriority): void {
  node.priority = priority;
  for (const child of node.children) {
    applyPriorityRecursive(child, priority);
  }
}

/**
 * Cascades priority to a node or directory path and all descendant nodes.
 */
export function cascadePriority(
  node: FileTreeNode,
  priority: FilePriority,
): void;
export function cascadePriority(
  tree: FileTreeNode[],
  dirPath: string,
  priority: FilePriority,
): void;
export function cascadePriority(
  target: FileTreeNode | FileTreeNode[],
  priorityOrPath: FilePriority | string,
  maybePriority?: FilePriority,
): void {
  if (Array.isArray(target)) {
    const dirPath = priorityOrPath as string;
    const prio = maybePriority!;
    const node = findNodeByPath(target, dirPath);
    if (node) {
      applyPriorityRecursive(node, prio);
    }
    return;
  }

  applyPriorityRecursive(target, priorityOrPath as FilePriority);
}

/**
 * Finds a node in the tree by its normalized path.
 */
export function findNodeByPath(
  nodes: FileTreeNode[],
  path: string,
): FileTreeNode | undefined {
  const normalized = path.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
  for (const node of nodes) {
    if (node.path === normalized) {
      return node;
    }
    if (node.children.length > 0) {
      const found = findNodeByPath(node.children, normalized);
      if (found) return found;
    }
  }
  return undefined;
}

/**
 * Collects all descendant file IDs under a node recursively.
 */
export function getDescendantFileIds(node: FileTreeNode): number[] {
  const ids: number[] = [];
  function traverse(n: FileTreeNode) {
    if (!n.isDir && n.fileId !== undefined) {
      ids.push(n.fileId);
    }
    for (const child of n.children) {
      traverse(child);
    }
  }
  traverse(node);
  return ids;
}

/**
 * Collects all descendant file indices under a node recursively.
 */
export function getDescendantFileIndices(node: FileTreeNode): number[] {
  const indices: number[] = [];
  function traverse(n: FileTreeNode) {
    if (!n.isDir && n.fileIndex !== undefined) {
      indices.push(n.fileIndex);
    }
    for (const child of n.children) {
      traverse(child);
    }
  }
  traverse(node);
  return indices;
}

/**
 * Collects all descendant file nodes under a node recursively.
 */
export function getDescendantFiles(node: FileTreeNode): FileTreeNode[] {
  const files: FileTreeNode[] = [];
  function traverse(n: FileTreeNode) {
    if (!n.isDir) {
      files.push(n);
    }
    for (const child of n.children) {
      traverse(child);
    }
  }
  traverse(node);
  return files;
}

/**
 * Calculates overall directory priority based on descendant files.
 */
export function getDirectoryPriority(
  node: FileTreeNode,
  priorities?: Record<number, FilePriority>,
): string {
  const files = getDescendantFiles(node);
  if (files.length === 0) return "Normal";
  const first = formatPriority(
    (files[0].fileId !== undefined && priorities?.[files[0].fileId] !== undefined)
      ? priorities[files[0].fileId]
      : files[0].priority,
  );
  const allSame = files.every((f) => {
    const p = formatPriority(
      (f.fileId !== undefined && priorities?.[f.fileId] !== undefined)
        ? priorities[f.fileId]
        : f.priority,
    );
    return p === first;
  });
  return allSame ? first : "Mixed";
}

/**
 * Normalizes priority value to human-readable format.
 */
export function formatPriority(prio: FilePriority | string | undefined): string {
  if (prio === undefined || prio === null) return "Normal";
  const str = String(prio).toLowerCase();
  if (str === "0" || str === "skip" || str === "do not download" || str === "skip all") {
    return "Do Not Download";
  }
  if (str === "2" || str === "high") {
    return "High";
  }
  return "Normal";
}
