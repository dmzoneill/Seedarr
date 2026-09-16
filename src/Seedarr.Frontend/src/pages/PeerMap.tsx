import { useRef, useEffect, useState, useCallback, useMemo } from "react";
import { useNavigate } from "react-router";
import * as d3 from "d3";
import { usePeerGraph, useTorrents } from "../api/hooks";
import type { PeerGraphNode } from "../api/types";

export interface SimNode extends d3.SimulationNodeDatum, PeerGraphNode {}
export interface SimLink extends d3.SimulationLinkDatum<SimNode> {
  type: string;
}

export interface TopologySnapshot {
  nodeIds: Set<string>;
  linkKeys: Set<string>;
}

export const getNodeId = (
  endpoint: string | number | SimNode | d3.SimulationNodeDatum | null | undefined,
): string => {
  if (typeof endpoint === "object" && endpoint !== null) {
    return (endpoint as SimNode).id ?? "";
  }
  if (typeof endpoint === "number") {
    return String(endpoint);
  }
  return typeof endpoint === "string" ? endpoint : "";
};

export function filterValidLinks<
  T extends {
    source: string | number | SimNode | d3.SimulationNodeDatum | null | undefined;
    target: string | number | SimNode | d3.SimulationNodeDatum | null | undefined;
  },
>(links: T[], nodes: { id: string }[]): T[] {
  const validNodeIdSet = new Set(nodes.map((n) => n.id));
  return links.filter((l) => {
    const s = getNodeId(l.source);
    const t = getNodeId(l.target);
    return validNodeIdSet.has(s) && validNodeIdSet.has(t);
  });
}

export function isTopologyChanged(
  prevTopology: TopologySnapshot | null,
  currentNodes: { id: string }[],
  currentLinks: { source: string | number | SimNode; target: string | number | SimNode }[],
): boolean {
  if (!prevTopology) return true;

  if (prevTopology.nodeIds.size !== currentNodes.length) return true;
  if (prevTopology.linkKeys.size !== currentLinks.length) return true;

  for (const node of currentNodes) {
    if (!prevTopology.nodeIds.has(node.id)) return true;
  }

  for (const link of currentLinks) {
    const s = getNodeId(link.source);
    const t = getNodeId(link.target);
    const key = `${s}->${t}`;
    if (!prevTopology.linkKeys.has(key)) return true;
  }

  return false;
}

export function mapPreservedNodes(
  rawNodes: PeerGraphNode[],
  prevNodes: Map<string, SimNode>,
  width: number,
  height: number,
  baseTorrentRadius: number,
): SimNode[] {
  let tIdx = 0;
  const numTorrents = rawNodes.filter((n) => n.type === "torrent").length;

  return rawNodes.map((n) => {
    const prev = prevNodes.get(n.id);
    const node: SimNode = prev
      ? {
          ...n,
          x: prev.x,
          y: prev.y,
          vx: prev.vx,
          vy: prev.vy,
          fx: prev.fx,
          fy: prev.fy,
        }
      : { ...n };

    if (node.type === "center") {
      node.fx = width / 2;
      node.fy = height / 2;
    } else if (node.type === "torrent" && prev == null) {
      const angle = (tIdx / (numTorrents || 1)) * 2 * Math.PI - Math.PI / 2;
      node.x = width / 2 + Math.cos(angle) * baseTorrentRadius;
      node.y = height / 2 + Math.sin(angle) * baseTorrentRadius;
      tIdx++;
    }
    return node;
  });
}

const NODE_COLORS: Record<string, string> = {
  center: "#c8a84e",
  torrent: "#27ae60",
  peer: "#3498db",
};

const LINK_COLORS: Record<string, string> = {
  seeds: "rgba(200, 168, 78, 0.6)",
  encrypted: "rgba(39, 174, 96, 0.6)",
  plain: "rgba(255, 255, 255, 0.2)",
};

function getTimeRange(hours: number): { start: string; end: string } {
  const end = new Date();
  const start = new Date(end.getTime() - hours * 60 * 60 * 1000);
  return {
    start: start.toISOString(),
    end: end.toISOString(),
  };
}

function PeerMap() {
  const svgRef = useRef<SVGSVGElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [hours, setHours] = useState(1);
  const [selectedTorrentFilter, setSelectedTorrentFilter] =
    useState<string>("all");
  const [selectedNode, setSelectedNode] = useState<SimNode | null>(null);
  const [dimensions, setDimensions] = useState({ width: 800, height: 600 });
  const navigate = useNavigate();

  const { data: torrentsList } = useTorrents();
  const range = useMemo(() => getTimeRange(hours), [hours]);
  const {
    data: graphData,
    isLoading,
    isError,
  } = usePeerGraph(range.start, range.end);

  const zoomRef = useRef<d3.ZoomBehavior<SVGSVGElement, unknown> | null>(null);

  const updateDimensions = useCallback(() => {
    if (containerRef.current) {
      const rect = containerRef.current.getBoundingClientRect();
      setDimensions({
        width: Math.max(rect.width, 400),
        height: Math.max(rect.height, 400),
      });
    }
  }, []);

  useEffect(() => {
    updateDimensions();
    window.addEventListener("resize", updateDimensions);
    return () => window.removeEventListener("resize", updateDimensions);
  }, [updateDimensions]);

  const simulationRef = useRef<d3.Simulation<SimNode, SimLink> | null>(null);
  const mainGroupRef = useRef<d3.Selection<SVGGElement, unknown, null, undefined> | null>(null);
  const linkGroupRef = useRef<d3.Selection<SVGGElement, unknown, null, undefined> | null>(null);
  const nodeGroupRef = useRef<d3.Selection<SVGGElement, unknown, null, undefined> | null>(null);
  const linksRef = useRef<SimLink[]>([]);
  const prevTopologyRef = useRef<TopologySnapshot | null>(null);
  const prevDimensionsRef = useRef<{ width: number; height: number } | null>(null);

  // Clean up simulation on unmount to prevent memory leaks
  useEffect(() => {
    return () => {
      if (simulationRef.current) {
        simulationRef.current.stop();
        simulationRef.current = null;
      }
    };
  }, []);

  // Initialize SVG container & zoom behavior once
  useEffect(() => {
    if (!svgRef.current) return;

    const svg = d3.select(svgRef.current);
    if (!mainGroupRef.current) {
      svg.selectAll("*").remove();

      const defs = svg.append("defs");
      defs
        .append("marker")
        .attr("id", "arrowhead")
        .attr("viewBox", "0 -5 10 10")
        .attr("refX", 22)
        .attr("refY", 0)
        .attr("markerWidth", 6)
        .attr("markerHeight", 6)
        .attr("orient", "auto")
        .append("path")
        .attr("d", "M0,-5L10,0L0,5")
        .attr("fill", "rgba(255, 255, 255, 0.4)");

      const g = svg.append("g").attr("class", "main-group");
      mainGroupRef.current = g;
      linkGroupRef.current = g.append("g").attr("class", "links");
      nodeGroupRef.current = g.append("g").attr("class", "nodes");

      const zoom = d3
        .zoom<SVGSVGElement, unknown>()
        .scaleExtent([0.1, 5])
        .on("zoom", (event) => {
          g.attr("transform", event.transform);
        });

      zoomRef.current = zoom;
      svg.call(zoom);
    }
  }, []);

  useEffect(() => {
    if (!graphData || !svgRef.current || !mainGroupRef.current || !linkGroupRef.current || !nodeGroupRef.current) return;

    const { width, height } = dimensions;

    // Filter nodes if a specific torrent is selected
    let rawNodes = graphData.nodes;
    let rawLinks = graphData.links;

    if (selectedTorrentFilter !== "all") {
      const targetTorrentNode = rawNodes.find(
        (n) =>
          n.id === selectedTorrentFilter ||
          n.infoHash === selectedTorrentFilter,
      );
      if (targetTorrentNode) {
        const connectedPeerIds = new Set<string>();
        rawLinks.forEach((l) => {
          const s = getNodeId(l.source);
          const t = getNodeId(l.target);
          if (s === targetTorrentNode.id) connectedPeerIds.add(t);
          if (t === targetTorrentNode.id) connectedPeerIds.add(s);
        });
        connectedPeerIds.add(targetTorrentNode.id);
        const centerId = rawNodes.find((n) => n.type === "center")?.id;
        if (centerId) connectedPeerIds.add(centerId);

        rawNodes = rawNodes.filter((n) => connectedPeerIds.has(n.id));
        rawLinks = rawLinks.filter(
          (l) =>
            connectedPeerIds.has(getNodeId(l.source)) &&
            connectedPeerIds.has(getNodeId(l.target)),
        );
      }
    }

    const prevNodes = new Map<string, SimNode>();
    if (simulationRef.current) {
      simulationRef.current.nodes().forEach((n) => prevNodes.set(n.id, n));
    }

    const numTorrents = rawNodes.filter((n) => n.type === "torrent").length;
    const numPeers = rawNodes.filter((n) => n.type === "peer").length;

    // Dynamic radius based on torrent count to avoid cluster overlap
    const baseTorrentRadius = Math.max(
      220,
      Math.min(width, height) * 0.35,
      numTorrents * 12,
    );
    const peerDistance = Math.max(90, Math.min(130, 800 / (numPeers || 1)));

    const nodes: SimNode[] = mapPreservedNodes(
      rawNodes,
      prevNodes,
      width,
      height,
      baseTorrentRadius,
    );

    const links: SimLink[] = filterValidLinks(
      rawLinks.map((l) => ({
        source: l.source,
        target: l.target,
        type: l.type,
      })),
      nodes,
    );
    linksRef.current = links;

    if (!simulationRef.current) {
      simulationRef.current = d3.forceSimulation<SimNode>();
    }

    const simulation = simulationRef.current;
    simulation
      .nodes(nodes)
      .force(
        "link",
        d3
          .forceLink<SimNode, SimLink>(links)
          .id((d) => d.id)
          .distance((d) =>
            d.type === "seeds" ? baseTorrentRadius : peerDistance,
          )
          .strength((d) => (d.type === "seeds" ? 0.6 : 0.8)),
      )
      .force(
        "charge",
        d3
          .forceManyBody<SimNode>()
          .strength((d) =>
            d.type === "center" ? -1200 : d.type === "torrent" ? -500 : -200,
          )
          .distanceMax(Math.max(width, height) * 1.5),
      )
      .force("center", d3.forceCenter(width / 2, height / 2).strength(0.06))
      .force(
        "collision",
        d3
          .forceCollide<SimNode>()
          .radius((d) =>
            d.type === "center" ? 48 : d.type === "torrent" ? 42 : 24,
          )
          .iterations(2),
      );

    const linkGroup = linkGroupRef.current;
    const nodeGroup = nodeGroupRef.current;

    // Persistent Link Data Join
    const link = linkGroup
      .selectAll<SVGLineElement, SimLink>("line")
      .data(links, (d) => {
        const s = getNodeId(d.source);
        const t = getNodeId(d.target);
        return `${s}->${t}`;
      })
      .join(
        (enter) =>
          enter
            .append("line")
            .attr("stroke", (d) => LINK_COLORS[d.type] || "rgba(255, 255, 255, 0.2)")
            .attr("stroke-width", (d) => (d.type === "seeds" ? 2 : 1.2))
            .attr("stroke-dasharray", (d) =>
              d.type === "encrypted" ? "4,4" : "none",
            )
            .attr("opacity", 0.7),
        (update) =>
          update
            .attr("stroke", (d) => LINK_COLORS[d.type] || "rgba(255, 255, 255, 0.2)")
            .attr("stroke-width", (d) => (d.type === "seeds" ? 2 : 1.2)),
        (exit) => exit.remove(),
      );

    const drag = d3
      .drag<SVGGElement, SimNode>()
      .on("start", (event, d) => {
        if (!event.active) simulation.alphaTarget(0.3).restart();
        d.fx = d.x;
        d.fy = d.y;
      })
      .on("drag", (event, d) => {
        d.fx = event.x;
        d.fy = event.y;
      })
      .on("end", (event, d) => {
        if (!event.active) simulation.alphaTarget(0);
        if (d.type !== "center") {
          d.fx = null;
          d.fy = null;
        }
      });

    // Persistent Node Data Join
    const node = nodeGroup
      .selectAll<SVGGElement, SimNode>("g.node")
      .data(nodes, (d) => d.id)
      .join(
        (enter) => {
          const g = enter
            .append("g")
            .attr("class", "node")
            .call(drag)
            .style("cursor", "pointer")
            .on("click", (event, d) => {
              event.stopPropagation();
              setSelectedNode(d);
            })
            .on("mouseenter", (_event, d) => {
              const neighborIds = new Set<string>();
              neighborIds.add(d.id);
              linksRef.current.forEach((l) => {
                const sId = getNodeId(l.source);
                const tId = getNodeId(l.target);
                if (sId === d.id) neighborIds.add(tId);
                if (tId === d.id) neighborIds.add(sId);
              });

              if (nodeGroupRef.current) {
                nodeGroupRef.current
                  .selectAll<SVGGElement, SimNode>("g.node")
                  .transition()
                  .duration(150)
                  .attr("opacity", (n) => (neighborIds.has(n.id) ? 1 : 0.2));
              }

              if (linkGroupRef.current) {
                linkGroupRef.current
                  .selectAll<SVGLineElement, SimLink>("line")
                  .transition()
                  .duration(150)
                  .attr("opacity", (l) => {
                    const sId = getNodeId(l.source);
                    const tId = getNodeId(l.target);
                    return sId === d.id || tId === d.id ? 1 : 0.05;
                  })
                  .attr("stroke-width", (l) => {
                    const sId = getNodeId(l.source);
                    const tId = getNodeId(l.target);
                    return sId === d.id || tId === d.id ? 2.5 : 1;
                  });
              }
            })
            .on("mouseleave", () => {
              if (nodeGroupRef.current) {
                nodeGroupRef.current
                  .selectAll<SVGGElement, SimNode>("g.node")
                  .transition()
                  .duration(150)
                  .attr("opacity", 1);
              }
              if (linkGroupRef.current) {
                linkGroupRef.current
                  .selectAll<SVGLineElement, SimLink>("line")
                  .transition()
                  .duration(150)
                  .attr("opacity", 0.7)
                  .attr("stroke-width", (d) => (d.type === "seeds" ? 2 : 1.2));
              }
            });

          g.append("circle")
            .attr("class", "node-circle")
            .attr("r", (d) => {
              if (d.type === "center") return 26;
              if (d.type === "torrent") return 15;
              return 9;
            })
            .attr("fill", (d) => NODE_COLORS[d.type] || "#666")
            .attr("stroke", "#111")
            .attr("stroke-width", 2)
            .attr("opacity", 0.95);

          g.append("text")
            .attr("class", "node-label")
            .text((d) => {
              if (d.label.length > 18) {
                return d.label.substring(0, 16) + "...";
              }
              return d.label;
            })
            .attr("text-anchor", "middle")
            .attr("dy", (d) => {
              if (d.type === "center") return 44;
              if (d.type === "torrent") return 28;
              return 22;
            })
            .attr("fill", "var(--text-primary)")
            .attr("stroke", "#0e0e0e")
            .attr("stroke-width", "3.5px")
            .attr("paint-order", "stroke fill")
            .attr("font-size", (d) => {
              if (d.type === "center") return "12px";
              if (d.type === "torrent") return "10px";
              return "8px";
            })
            .attr("font-weight", (d) => (d.type === "center" ? 700 : 600))
            .attr("font-family", "inherit");

          g.filter((d) => d.type === "center")
            .append("text")
            .attr("class", "node-center-icon")
            .text("⬢")
            .attr("text-anchor", "middle")
            .attr("dy", 6)
            .attr("fill", "#111")
            .attr("font-size", "22px");

          g.filter((d) => d.type === "torrent")
            .append("text")
            .attr("class", "node-torrent-icon")
            .text("■")
            .attr("text-anchor", "middle")
            .attr("dy", 5)
            .attr("fill", "#111")
            .attr("font-size", "12px");

          g.filter((d) => d.isEncrypted === true)
            .append("circle")
            .attr("class", "node-encrypted-dot")
            .attr("cx", (d) => (d.type === "peer" ? 8 : 15))
            .attr("cy", (d) => (d.type === "peer" ? -8 : -15))
            .attr("r", 5)
            .attr("fill", "#27ae60");

          g.append("title").text((d) => {
            if (d.type === "center") return "Seedarr Instance (Click for details)";
            if (d.type === "torrent")
              return `Torrent: ${d.label}\n${d.infoHash || ""}\n(Click to view details)`;
            return `Peer: ${d.label}${d.isEncrypted ? " (encrypted)" : ""}\n(Click for details)`;
          });

          return g;
        },
        (update) => {
          update
            .select(".node-label")
            .text((d) => {
              if (d.label.length > 18) {
                return d.label.substring(0, 16) + "...";
              }
              return d.label;
            });
          update.select("title").text((d) => {
            if (d.type === "center") return "Seedarr Instance (Click for details)";
            if (d.type === "torrent")
              return `Torrent: ${d.label}\n${d.infoHash || ""}\n(Click to view details)`;
            return `Peer: ${d.label}${d.isEncrypted ? " (encrypted)" : ""}\n(Click for details)`;
          });
          return update;
        },
        (exit) => exit.remove(),
      );

    simulation.on("tick", () => {
      link
        .attr("x1", (d) =>
          typeof d.source === "object" && d.source !== null
            ? ((d.source as SimNode).x ?? 0)
            : 0,
        )
        .attr("y1", (d) =>
          typeof d.source === "object" && d.source !== null
            ? ((d.source as SimNode).y ?? 0)
            : 0,
        )
        .attr("x2", (d) =>
          typeof d.target === "object" && d.target !== null
            ? ((d.target as SimNode).x ?? 0)
            : 0,
        )
        .attr("y2", (d) =>
          typeof d.target === "object" && d.target !== null
            ? ((d.target as SimNode).y ?? 0)
            : 0,
        );

      node.attr("transform", (d) => `translate(${d.x ?? 0},${d.y ?? 0})`);
    });

    const topologyChanged = isTopologyChanged(
      prevTopologyRef.current,
      nodes,
      links,
    );
    const dimensionsChanged =
      !prevDimensionsRef.current ||
      prevDimensionsRef.current.width !== width ||
      prevDimensionsRef.current.height !== height;

    const currentNodeIds = new Set(nodes.map((n) => n.id));
    const currentLinkKeys = new Set(
      links.map((l) => `${getNodeId(l.source)}->${getNodeId(l.target)}`),
    );
    prevTopologyRef.current = {
      nodeIds: currentNodeIds,
      linkKeys: currentLinkKeys,
    };
    prevDimensionsRef.current = { width, height };

    if (topologyChanged || dimensionsChanged) {
      simulation.alpha(0.3).restart();
    }
  }, [graphData, dimensions, selectedTorrentFilter]);

  const handleZoomIn = () => {
    if (svgRef.current && zoomRef.current) {
      d3.select(svgRef.current)
        .transition()
        .duration(250)
        .call(zoomRef.current.scaleBy, 1.35);
    }
  };

  const handleZoomOut = () => {
    if (svgRef.current && zoomRef.current) {
      d3.select(svgRef.current)
        .transition()
        .duration(250)
        .call(zoomRef.current.scaleBy, 0.75);
    }
  };

  const handleResetZoom = () => {
    if (svgRef.current && zoomRef.current) {
      d3.select(svgRef.current)
        .transition()
        .duration(350)
        .call(zoomRef.current.transform, d3.zoomIdentity);
    }
  };

  const torrentCount =
    graphData?.nodes.filter((n) => n.type === "torrent").length ?? 0;
  const peerCount =
    graphData?.nodes.filter((n) => n.type === "peer").length ?? 0;

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: 0,
        overflow: "hidden",
        padding: "1.5rem",
        boxSizing: "border-box",
      }}
    >
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
          flexShrink: 0,
        }}
      >
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
            <h1
              style={{
                fontSize: "1.75rem",
                fontWeight: 700,
                margin: 0,
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
              }}
            >
              <span>🗺️</span> Peer Map
            </h1>
            <span className="badge badge-primary">
              {peerCount} Connected Peers
            </span>
          </div>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Live swarm topology visualization connecting Seedarr, active torrents, and remote peers
          </p>
        </div>
      </div>

      {/* Control Toolbar */}
      <div className="peer-map-controls" style={{ flexShrink: 0 }}>
        <div style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}>
          <label className="peer-map-label">Time Range:</label>
          {[1, 6, 12, 24].map((h) => (
            <button
              key={h}
              className={`peer-map-btn ${hours === h ? "peer-map-btn-active" : ""}`}
              onClick={() => setHours(h)}
            >
              {h}h
            </button>
          ))}
        </div>

        {/* Swarm Focus Filter */}
        <div style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}>
          <label className="peer-map-label">Filter Swarm:</label>
          <select
            value={selectedTorrentFilter}
            onChange={(e) => setSelectedTorrentFilter(e.target.value)}
            style={{
              padding: "0.35rem 0.65rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light)",
              backgroundColor: "var(--bg-primary)",
              color: "inherit",
              fontSize: "0.82rem",
            }}
          >
            <option value="all">All Torrents ({torrentCount})</option>
            {torrentsList?.map((t) => (
              <option key={t.id} value={t.infoHash}>
                {t.name}
              </option>
            ))}
          </select>
        </div>

        <span className="peer-map-stats">
          📊 {torrentCount} swarms • 👥 {peerCount} peers
        </span>
      </div>

      {/* Legend */}
      <div className="peer-map-legend" style={{ flexShrink: 0 }}>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-dot"
            style={{ background: NODE_COLORS.center }}
          />
          Seedarr
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-dot"
            style={{ background: NODE_COLORS.torrent }}
          />
          Torrent
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-dot"
            style={{ background: NODE_COLORS.peer }}
          />
          Peer
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-line"
            style={{
              borderColor: "#27ae60",
              borderStyle: "dashed",
            }}
          />
          Encrypted
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-line"
            style={{ borderColor: "rgba(255, 255, 255, 0.4)" }}
          />
          Plain
        </span>
      </div>

      {/* D3 Simulation Container */}
      <div
        ref={containerRef}
        className="peer-map-container"
        style={{
          position: "relative",
          flex: "1 1 auto",
          minHeight: 0,
          height: "100%",
        }}
      >
        {isLoading && (
          <div className="peer-map-loading">Loading peer topology...</div>
        )}
        {!isLoading && isError && (
          <div className="peer-map-empty" style={{ color: "var(--danger)" }}>
            Failed to load peer topology data.
          </div>
        )}
        {!isLoading && !isError && peerCount === 0 && (
          <div className="peer-map-empty">
            No active peer connections in the selected time range.
          </div>
        )}
        <svg
          ref={svgRef}
          width={dimensions.width}
          height={dimensions.height}
          className="peer-map-svg"
          onClick={() => setSelectedNode(null)}
        />

        {/* Floating Zoom Controls */}
        <div
          style={{
            position: "absolute",
            top: "1rem",
            right: "1rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.35rem",
            zIndex: 5,
          }}
        >
          <button
            className="btn btn-outline"
            style={{
              padding: "0.35rem 0.65rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "rgba(20, 20, 20, 0.85)",
              backdropFilter: "blur(4px)",
            }}
            onClick={handleZoomIn}
            title="Zoom In"
          >
            ➕
          </button>
          <button
            className="btn btn-outline"
            style={{
              padding: "0.35rem 0.65rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "rgba(20, 20, 20, 0.85)",
              backdropFilter: "blur(4px)",
            }}
            onClick={handleZoomOut}
            title="Zoom Out"
          >
            ➖
          </button>
          <button
            className="btn btn-outline"
            style={{
              padding: "0.35rem 0.65rem",
              fontSize: "0.85rem",
              borderRadius: "6px",
              backgroundColor: "rgba(20, 20, 20, 0.85)",
              backdropFilter: "blur(4px)",
            }}
            onClick={handleResetZoom}
            title="Reset Zoom & Center"
          >
            ⟲
          </button>
        </div>

        {/* Selected Node Details Flyout */}
        {selectedNode && (
          <div
            className="card"
            style={{
              position: "absolute",
              bottom: "1.25rem",
              right: "1.25rem",
              width: "300px",
              padding: "1.25rem",
              backgroundColor: "rgba(22, 22, 22, 0.95)",
              backdropFilter: "blur(8px)",
              border: "1px solid var(--border-light)",
              borderRadius: "8px",
              boxShadow: "0 12px 35px rgba(0, 0, 0, 0.7)",
              zIndex: 10,
            }}
            onClick={(e) => e.stopPropagation()}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "0.5rem",
              }}
            >
              <span
                className="badge"
                style={{
                  backgroundColor: NODE_COLORS[selectedNode.type] || "#666",
                  color: "#fff",
                  fontSize: "0.75rem",
                  textTransform: "uppercase",
                  borderRadius: "4px",
                }}
              >
                {selectedNode.type}
              </span>
              <button
                className="btn btn-small"
                style={{
                  border: "none",
                  background: "none",
                  fontSize: "0.85rem",
                  cursor: "pointer",
                  color: "var(--text-muted)",
                }}
                onClick={() => setSelectedNode(null)}
              >
                ✕
              </button>
            </div>

            <div
              style={{
                fontWeight: 600,
                fontSize: "0.95rem",
                wordBreak: "break-word",
                marginBottom: "0.5rem",
              }}
            >
              {selectedNode.label}
            </div>

            {selectedNode.infoHash && (
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-muted)",
                  marginBottom: "0.75rem",
                }}
              >
                <code style={{ wordBreak: "break-all" }}>
                  {selectedNode.infoHash}
                </code>
              </div>
            )}

            {selectedNode.type === "torrent" && (
              <button
                className="btn btn-small btn-primary"
                style={{
                  width: "100%",
                  marginTop: "0.5rem",
                  borderRadius: "6px",
                }}
                onClick={() => navigate("/torrents")}
              >
                Open in Torrents View →
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

export default PeerMap;
