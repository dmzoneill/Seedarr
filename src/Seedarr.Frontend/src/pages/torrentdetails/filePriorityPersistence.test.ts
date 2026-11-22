import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { buildFileTree } from "../../utils/fileTree";
import {
  API_FILE_PRIORITY,
  createFilePriorityHandlers,
  toApiFilePriority,
  type BatchFilePriorityRequest,
  type FilePriorityState,
  type SingleFilePriorityRequest,
} from "./filePriorityPersistence";

interface RecordedCall {
  kind: "single" | "batch";
  request: SingleFilePriorityRequest | BatchFilePriorityRequest;
}

function harness(initial?: FilePriorityState) {
  let state: FilePriorityState = initial ?? { priorities: {}, wanted: {} };
  const calls: RecordedCall[] = [];
  const errors: string[] = [];

  const handlers = createFilePriorityHandlers({
    torrentId: 42,
    getState: () => state,
    setState: (next) => {
      state = next;
    },
    setFilePriority: (request, options) => {
      calls.push({ kind: "single", request });
      if (request.fileId < 0) {
        options?.onError?.(new Error("save failed"));
      }
    },
    setFilesPriority: (request) => {
      calls.push({ kind: "batch", request });
    },
    onError: (message) => {
      errors.push(message);
    },
  });

  return {
    handlers,
    calls,
    errors,
    get state() {
      return state;
    },
  };
}

void describe("torrent details FilesTab priority persistence", () => {
  void it("maps the page labels onto priorities the API stores", () => {
    assert.equal(toApiFilePriority("Do Not Download"), API_FILE_PRIORITY.skip);
    assert.equal(toApiFilePriority("Skip All"), API_FILE_PRIORITY.skip);
    assert.equal(toApiFilePriority("High"), API_FILE_PRIORITY.high);
    assert.equal(toApiFilePriority("Normal"), API_FILE_PRIORITY.normal);
    assert.equal(toApiFilePriority(0), API_FILE_PRIORITY.skip);
    assert.equal(toApiFilePriority(6), API_FILE_PRIORITY.high);
  });

  void it("persists a single file priority and wanted flag", () => {
    const ui = harness();

    ui.handlers.handleFilePriority(7, "High");

    assert.deepEqual(ui.state.priorities, { 7: "High" });
    assert.deepEqual(ui.state.wanted, { 7: true });
    assert.deepEqual(ui.calls, [
      {
        kind: "single",
        request: {
          torrentId: 42,
          fileId: 7,
          priority: API_FILE_PRIORITY.high,
        },
      },
    ]);

    ui.handlers.handleFileWanted(7, false);

    assert.equal(ui.state.priorities[7], "Do Not Download");
    assert.equal(ui.state.wanted[7], false);
    assert.equal(ui.calls[1].kind, "single");
    assert.deepEqual(ui.calls[1].request, {
      torrentId: 42,
      fileId: 7,
      priority: API_FILE_PRIORITY.skip,
    });
  });

  void it("persists a directory priority change as one batch request", () => {
    const tree = buildFileTree([
      { id: 1, path: "Season/a.mkv", size: 10 },
      { id: 2, path: "Season/b.mkv", size: 10 },
    ]);
    const ui = harness();

    ui.handlers.handleDirectoryPriority(tree[0], "Do Not Download");

    assert.deepEqual(ui.state.priorities, {
      1: "Do Not Download",
      2: "Do Not Download",
    });
    assert.deepEqual(ui.state.wanted, { 1: false, 2: false });
    assert.equal(ui.calls.length, 1);
    assert.deepEqual(ui.calls[0].request, {
      torrentId: 42,
      files: [
        { fileId: 1, priority: API_FILE_PRIORITY.skip },
        { fileId: 2, priority: API_FILE_PRIORITY.skip },
      ],
    });
  });

  void it("persists a directory wanted toggle for every descendant file", () => {
    const tree = buildFileTree([
      { id: 3, path: "Movie/video.mkv", size: 10 },
      { id: 4, path: "Movie/sub.srt", size: 1 },
    ]);
    const ui = harness({
      priorities: { 3: "High", 4: "Normal" },
      wanted: { 3: true, 4: true },
    });

    ui.handlers.handleDirectoryWanted(tree[0], false);

    assert.equal(ui.state.wanted[3], false);
    assert.equal(ui.state.wanted[4], false);
    assert.equal(ui.calls[0].kind, "batch");
    const request = ui.calls[0].request as BatchFilePriorityRequest;
    assert.deepEqual(
      request.files.map((file) => file.priority),
      [API_FILE_PRIORITY.skip, API_FILE_PRIORITY.skip],
    );

    ui.handlers.handleDirectoryWanted(tree[0], true);
    const restored = ui.calls[1].request as BatchFilePriorityRequest;
    assert.deepEqual(
      restored.files.map((file) => file.priority),
      [API_FILE_PRIORITY.normal, API_FILE_PRIORITY.normal],
    );
    assert.equal(ui.state.priorities[3], "Normal");
    assert.equal(ui.state.wanted[3], true);
  });

  void it("rolls back the optimistic state and reports the error when the save fails", () => {
    const ui = harness({
      priorities: { 7: "Normal" },
      wanted: { 7: true },
    });

    ui.handlers.handleFilePriority(-5, "High");

    assert.deepEqual(ui.state, {
      priorities: { 7: "Normal" },
      wanted: { 7: true },
    });
    assert.deepEqual(ui.errors, ["save failed"]);
  });

  void it("does not send a request for a directory with no files", () => {
    const ui = harness();
    ui.handlers.handleDirectoryPriority(
      {
        name: "empty",
        path: "empty",
        size: 0,
        isDir: true,
        children: [],
      },
      "High",
    );

    assert.equal(ui.calls.length, 0);
    assert.deepEqual(ui.state, { priorities: {}, wanted: {} });
  });
});
