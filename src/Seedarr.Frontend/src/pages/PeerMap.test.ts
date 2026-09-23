import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { PeerGraphNode } from "../api/types";
import {
  filterValidLinks,
  getNodeId,
  isTopologyChanged,
  mapPreservedNodes,
  type SimNode,
} from "./PeerMap";

describe("PeerMap: getNodeId", () => {
  it("extracts node ID from string endpoint", () => {
    assert.equal(getNodeId("peer-1"), "peer-1");
  });

  it("extracts node ID from SimNode object reference", () => {
    const nodeObj: SimNode = {
      id: "torrent-ubuntu",
      label: "Ubuntu",
      type: "torrent",
      x: 100,
      y: 200,
    };
    assert.equal(getNodeId(nodeObj), "torrent-ubuntu");
    assert.notEqual(getNodeId(nodeObj), "[object Object]");
  });

  it("handles numeric indices safely", () => {
    assert.equal(getNodeId(0), "0");
    assert.equal(getNodeId(42), "42");
  });

  it("handles null and undefined gracefully without throwing", () => {
    assert.equal(getNodeId(null), "");
    assert.equal(getNodeId(undefined), "");
  });
});

describe("PeerMap: mapPreservedNodes", () => {
  it("preserves fx and fy coordinates for dragged/pinned nodes across polls", () => {
    const rawNodes: PeerGraphNode[] = [
      { id: "peer-1", label: "192.168.1.50:51413", type: "peer" },
      { id: "peer-2", label: "10.0.0.2:6881", type: "peer" },
    ];

    const prevNodes = new Map<string, SimNode>();
    prevNodes.set("peer-1", {
      id: "peer-1",
      label: "192.168.1.50:51413",
      type: "peer",
      x: 150,
      y: 250,
      vx: 0.1,
      vy: -0.2,
      fx: 155, // User is actively dragging this node!
      fy: 255,
    });

    const mapped = mapPreservedNodes(rawNodes, prevNodes, 800, 600, 200);

    const peer1 = mapped.find((n) => n.id === "peer-1");
    assert.ok(peer1);
    assert.equal(peer1.x, 150);
    assert.equal(peer1.y, 250);
    assert.equal(
      peer1.fx,
      155,
      "fx must be preserved from previous drag state",
    );
    assert.equal(
      peer1.fy,
      255,
      "fy must be preserved from previous drag state",
    );

    const peer2 = mapped.find((n) => n.id === "peer-2");
    assert.ok(peer2);
    assert.equal(peer2.fx, undefined);
    assert.equal(peer2.fy, undefined);
  });

  it("pins the center node to width/2 and height/2", () => {
    const rawNodes: PeerGraphNode[] = [
      { id: "center-node", label: "Seedarr", type: "center" },
    ];
    const prevNodes = new Map<string, SimNode>();

    const mapped = mapPreservedNodes(rawNodes, prevNodes, 1000, 800, 200);
    const center = mapped[0];

    assert.equal(center.fx, 500);
    assert.equal(center.fy, 400);
  });
});

describe("PeerMap: isTopologyChanged", () => {
  it("returns true on initial mount when prevTopology is null", () => {
    const currentNodes = [{ id: "node-1" }, { id: "node-2" }];
    const currentLinks = [{ source: "node-1", target: "node-2" }];

    assert.equal(isTopologyChanged(null, currentNodes, currentLinks), true);
  });

  it("returns false when nodes and links remain identical across polls", () => {
    const currentNodes = [{ id: "node-1" }, { id: "node-2" }];
    const currentLinks = [{ source: "node-1", target: "node-2" }];

    const snapshot = {
      nodeIds: new Set(["node-1", "node-2"]),
      linkKeys: new Set(["node-1->node-2"]),
    };

    assert.equal(
      isTopologyChanged(snapshot, currentNodes, currentLinks),
      false,
    );
  });

  it("returns false when links have been mutated by D3 into node object references", () => {
    const currentNodes = [{ id: "node-1" }, { id: "node-2" }];
    const currentLinks = [
      {
        source: { id: "node-1", label: "N1", type: "peer" } as SimNode,
        target: { id: "node-2", label: "N2", type: "torrent" } as SimNode,
      },
    ];

    const snapshot = {
      nodeIds: new Set(["node-1", "node-2"]),
      linkKeys: new Set(["node-1->node-2"]),
    };

    assert.equal(
      isTopologyChanged(snapshot, currentNodes, currentLinks),
      false,
    );
  });

  it("returns true when a node is added or removed", () => {
    const snapshot = {
      nodeIds: new Set(["node-1", "node-2"]),
      linkKeys: new Set(["node-1->node-2"]),
    };

    // Node added
    const addedNodes = [{ id: "node-1" }, { id: "node-2" }, { id: "node-3" }];
    const currentLinks = [{ source: "node-1", target: "node-2" }];
    assert.equal(isTopologyChanged(snapshot, addedNodes, currentLinks), true);

    // Node removed
    const removedNodes = [{ id: "node-1" }];
    assert.equal(isTopologyChanged(snapshot, removedNodes, currentLinks), true);
  });

  it("returns true when a link is added or removed", () => {
    const snapshot = {
      nodeIds: new Set(["node-1", "node-2", "node-3"]),
      linkKeys: new Set(["node-1->node-2"]),
    };

    const currentNodes = [{ id: "node-1" }, { id: "node-2" }, { id: "node-3" }];

    // Link added
    const addedLinks = [
      { source: "node-1", target: "node-2" },
      { source: "node-2", target: "node-3" },
    ];
    assert.equal(isTopologyChanged(snapshot, currentNodes, addedLinks), true);
  });
});

describe("PeerMap: filterValidLinks", () => {
  it("keeps links where both source and target exist in nodes", () => {
    const nodes = [{ id: "center" }, { id: "torrent-1" }, { id: "peer-1" }];
    const links = [
      { source: "center", target: "torrent-1" },
      { source: "torrent-1", target: "peer-1" },
    ];

    const result = filterValidLinks(links, nodes);
    assert.equal(result.length, 2);
  });

  it("filters out links referencing non-existent source node", () => {
    const nodes = [{ id: "torrent-1" }, { id: "peer-1" }];
    const links = [
      { source: "non-existent-source", target: "torrent-1" },
      { source: "torrent-1", target: "peer-1" },
    ];

    const result = filterValidLinks(links, nodes);
    assert.equal(result.length, 1);
    assert.equal(result[0].source, "torrent-1");
    assert.equal(result[0].target, "peer-1");
  });

  it("filters out links referencing non-existent target node", () => {
    const nodes = [{ id: "torrent-1" }];
    const links = [{ source: "torrent-1", target: "missing-peer" }];

    const result = filterValidLinks(links, nodes);
    assert.equal(result.length, 0);
  });

  it("handles object node references for source and target", () => {
    const nodes = [{ id: "torrent-1" }, { id: "peer-1" }];
    const links = [
      {
        source: { id: "torrent-1", label: "T1", type: "torrent" } as SimNode,
        target: { id: "peer-1", label: "P1", type: "peer" } as SimNode,
      },
      {
        source: { id: "torrent-1", label: "T1", type: "torrent" } as SimNode,
        target: { id: "missing", label: "M", type: "peer" } as SimNode,
      },
    ];

    const result = filterValidLinks(links, nodes);
    assert.equal(result.length, 1);
    assert.equal(getNodeId(result[0].target), "peer-1");
  });
});
