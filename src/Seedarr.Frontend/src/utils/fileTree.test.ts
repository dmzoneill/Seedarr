import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  buildFileTree,
  cascadePriority,
  findNodeByPath,
  getDescendantFileIds,
  getDescendantFiles,
  getDirectoryPriority,
  formatPriority,
} from "./fileTree";

describe("fileTree: buildFileTree and cascading priority", () => {
  it("should correctly split Windows paths with backslashes into directory and child file", () => {
    const files = [
      {
        id: 1,
        torrentId: 10,
        path: "Season 01\\Episode 01.mkv",
        size: 500_000_000,
        pieceOffset: 0,
        pieceCount: 500,
      },
    ];

    const tree = buildFileTree(files);

    assert.equal(tree.length, 1);
    const seasonDir = tree[0];
    assert.equal(seasonDir.name, "Season 01");
    assert.equal(seasonDir.path, "Season 01");
    assert.equal(seasonDir.isDir, true);
    assert.equal(seasonDir.size, 500_000_000);
    assert.equal(seasonDir.children.length, 1);

    const epFile = seasonDir.children[0];
    assert.equal(epFile.name, "Episode 01.mkv");
    assert.equal(epFile.path, "Season 01/Episode 01.mkv");
    assert.equal(epFile.isDir, false);
    assert.equal(epFile.size, 500_000_000);
    assert.equal(epFile.fileId, 1);
  });

  it("should not produce empty ghost nodes with leading, trailing, and duplicate slashes", () => {
    const files = [
      {
        id: 101,
        torrentId: 10,
        path: "/Movie//Sub/file.mkv/",
        size: 1_000_000,
        pieceOffset: 0,
        pieceCount: 1,
      },
    ];

    const tree = buildFileTree(files);

    // Root should only have "Movie"
    assert.equal(tree.length, 1);
    const movieDir = tree[0];
    assert.equal(movieDir.name, "Movie");
    assert.equal(movieDir.path, "Movie");
    assert.equal(movieDir.isDir, true);

    // Movie should have 1 child "Sub"
    assert.equal(movieDir.children.length, 1);
    const subDir = movieDir.children[0];
    assert.equal(subDir.name, "Sub");
    assert.equal(subDir.path, "Movie/Sub");
    assert.equal(subDir.isDir, true);

    // Sub should have 1 child "file.mkv"
    assert.equal(subDir.children.length, 1);
    const file = subDir.children[0];
    assert.equal(file.name, "file.mkv");
    assert.equal(file.path, "Movie/Sub/file.mkv");
    assert.equal(file.isDir, false);
    assert.equal(file.fileId, 101);
  });

  it("should update all descendant file nodes under a directory recursively when cascading priority", () => {
    const files = [
      { id: 1, path: "Series/Season 01/ep1.mkv", size: 100 },
      { id: 2, path: "Series/Season 01/ep2.mkv", size: 100 },
      { id: 3, path: "Series/Season 02/ep1.mkv", size: 100 },
      { id: 4, path: "Series/extras/trailer.mp4", size: 50 },
    ];

    const tree = buildFileTree(files, { defaultPriority: "Normal" });
    const seriesDir = findNodeByPath(tree, "Series");
    assert.ok(seriesDir);

    // Cascade High to whole series
    cascadePriority(seriesDir, "High");

    const allDescendantFiles = getDescendantFiles(seriesDir);
    assert.equal(allDescendantFiles.length, 4);
    for (const f of allDescendantFiles) {
      assert.equal(f.priority, "High");
    }

    // Cascade Do Not Download to Season 01 only
    const season1Dir = findNodeByPath(tree, "Series/Season 01");
    assert.ok(season1Dir);
    cascadePriority(season1Dir, "Do Not Download");

    assert.equal(season1Dir.children[0].priority, "Do Not Download");
    assert.equal(season1Dir.children[1].priority, "Do Not Download");

    // Season 02 and extras should still be High
    const season2Dir = findNodeByPath(tree, "Series/Season 02");
    assert.ok(season2Dir);
    assert.equal(season2Dir.children[0].priority, "High");

    const extrasDir = findNodeByPath(tree, "Series/extras");
    assert.ok(extrasDir);
    assert.equal(extrasDir.children[0].priority, "High");
  });

  it("should collect descendant file ids correctly", () => {
    const files = [
      { id: 10, path: "Dir/A/f1.txt", size: 10 },
      { id: 20, path: "Dir/A/f2.txt", size: 20 },
      { id: 30, path: "Dir/B/f3.txt", size: 30 },
    ];

    const tree = buildFileTree(files);
    const dirNode = tree[0];
    const ids = getDescendantFileIds(dirNode);

    assert.deepEqual(ids.sort((a, b) => a - b), [10, 20, 30]);

    const aNode = findNodeByPath(tree, "Dir/A")!;
    assert.deepEqual(getDescendantFileIds(aNode).sort((a, b) => a - b), [10, 20]);
  });

  it("should calculate directory priority as Mixed or uniform", () => {
    const files = [
      { id: 1, path: "Folder/f1.txt", size: 10 },
      { id: 2, path: "Folder/f2.txt", size: 20 },
    ];

    const tree = buildFileTree(files);
    const folder = tree[0];

    // Uniform Normal
    assert.equal(getDirectoryPriority(folder), "Normal");

    // Mixed
    cascadePriority(folder.children[0], "High");
    assert.equal(getDirectoryPriority(folder), "Mixed");

    // Uniform High
    cascadePriority(folder.children[1], "High");
    assert.equal(getDirectoryPriority(folder), "High");
  });

  it("should format priorities correctly", () => {
    assert.equal(formatPriority("skip"), "Do Not Download");
    assert.equal(formatPriority("Do Not Download"), "Do Not Download");
    assert.equal(formatPriority(0), "Do Not Download");
    assert.equal(formatPriority("high"), "High");
    assert.equal(formatPriority(2), "High");
    assert.equal(formatPriority("normal"), "Normal");
    assert.equal(formatPriority(1), "Normal");
    assert.equal(formatPriority(undefined), "Normal");
  });
});
