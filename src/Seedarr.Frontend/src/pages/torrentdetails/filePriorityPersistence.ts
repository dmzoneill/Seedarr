import { trackFilePriorityChange } from "../../utils/analytics";
import {
  formatPriority,
  getDescendantFileIds,
  type FilePriority,
  type FileTreeNode,
} from "../../utils/fileTree";

/** qBittorrent/libtorrent values that both file views render as the same label. */
export const API_FILE_PRIORITY = {
  skip: 0,
  normal: 3,
  high: 6,
} as const;

export interface FilePriorityState {
  priorities: Record<number, FilePriority>;
  wanted: Record<number, boolean>;
}

export interface FilePriorityMutationOptions {
  onError?: (error: unknown) => void;
}

export interface SingleFilePriorityRequest {
  torrentId: number;
  fileId: number;
  priority: number;
}

export interface BatchFilePriorityRequest {
  torrentId: number;
  files: Array<{ fileId: number; priority: number }>;
}

export interface FilePriorityHandlerDeps {
  torrentId: number;
  getState: () => FilePriorityState;
  setState: (state: FilePriorityState) => void;
  setFilePriority: (
    request: SingleFilePriorityRequest,
    options?: FilePriorityMutationOptions,
  ) => void;
  setFilesPriority: (
    request: BatchFilePriorityRequest,
    options?: FilePriorityMutationOptions,
  ) => void;
  onError?: (message: string) => void;
}

export function toApiFilePriority(priority: FilePriority): number {
  const formatted = formatPriority(priority);
  if (formatted === "Do Not Download") {
    return API_FILE_PRIORITY.skip;
  }
  if (formatted === "High") {
    return API_FILE_PRIORITY.high;
  }
  return API_FILE_PRIORITY.normal;
}

export function priorityForWanted(wanted: boolean): FilePriority {
  return wanted ? "Normal" : "Do Not Download";
}

function analyticsPriority(
  apiPriority: number,
): "skip" | "high" | "normal" | "low" {
  if (apiPriority === API_FILE_PRIORITY.skip) {
    return "skip";
  }
  if (apiPriority === API_FILE_PRIORITY.high) {
    return "high";
  }
  return "normal";
}

function errorMessage(error: unknown): string {
  return error instanceof Error
    ? error.message
    : "Failed to update file priority";
}

function rollbackIds(
  current: FilePriorityState,
  previous: FilePriorityState,
  optimistic: FilePriorityState,
  fileIds: number[],
): FilePriorityState | null {
  const priorities = { ...current.priorities };
  const wanted = { ...current.wanted };
  let changed = false;

  for (const id of fileIds) {
    if (
      priorities[id] !== optimistic.priorities[id] ||
      wanted[id] !== optimistic.wanted[id]
    ) {
      continue;
    }

    if (Object.prototype.hasOwnProperty.call(previous.priorities, id)) {
      priorities[id] = previous.priorities[id];
    } else {
      delete priorities[id];
    }

    if (Object.prototype.hasOwnProperty.call(previous.wanted, id)) {
      wanted[id] = previous.wanted[id];
    } else {
      delete wanted[id];
    }

    changed = true;
  }

  return changed ? { priorities, wanted } : null;
}

function applyPriority(
  deps: FilePriorityHandlerDeps,
  fileIds: number[],
  priority: FilePriority,
): void {
  const ids = fileIds.filter((id) => Number.isFinite(id));
  if (deps.torrentId <= 0 || ids.length === 0) {
    return;
  }

  const previous = deps.getState();
  const isWanted = formatPriority(priority) !== "Do Not Download";
  const priorities = { ...previous.priorities };
  const wanted = { ...previous.wanted };
  for (const id of ids) {
    priorities[id] = priority;
    wanted[id] = isWanted;
  }
  const optimistic = { priorities, wanted };
  deps.setState(optimistic);

  const apiPriority = toApiFilePriority(priority);
  trackFilePriorityChange(analyticsPriority(apiPriority), ids.length);

  const options: FilePriorityMutationOptions = {
    onError: (error) => {
      const rolledBack = rollbackIds(
        deps.getState(),
        previous,
        optimistic,
        ids,
      );
      if (rolledBack) {
        deps.setState(rolledBack);
      }
      deps.onError?.(errorMessage(error));
    },
  };

  if (ids.length === 1) {
    deps.setFilePriority(
      { torrentId: deps.torrentId, fileId: ids[0], priority: apiPriority },
      options,
    );
    return;
  }

  deps.setFilesPriority(
    {
      torrentId: deps.torrentId,
      files: ids.map((fileId) => ({ fileId, priority: apiPriority })),
    },
    options,
  );
}

export function createFilePriorityHandlers(deps: FilePriorityHandlerDeps): {
  handleFilePriority: (fileId: number, priority: FilePriority) => void;
  handleFileWanted: (fileId: number, wanted: boolean) => void;
  handleDirectoryPriority: (node: FileTreeNode, priority: FilePriority) => void;
  handleDirectoryWanted: (node: FileTreeNode, wanted: boolean) => void;
} {
  return {
    handleFilePriority: (fileId, priority) => {
      applyPriority(deps, [fileId], priority);
    },
    handleFileWanted: (fileId, wanted) => {
      applyPriority(deps, [fileId], priorityForWanted(wanted));
    },
    handleDirectoryPriority: (node, priority) => {
      applyPriority(deps, getDescendantFileIds(node), priority);
    },
    handleDirectoryWanted: (node, wanted) => {
      applyPriority(
        deps,
        getDescendantFileIds(node),
        priorityForWanted(wanted),
      );
    },
  };
}
