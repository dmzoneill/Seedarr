import { useRef, useEffect, useState, useCallback, useMemo } from "react";
import * as d3 from "d3";
import { usePeerGraph } from "../api/hooks";
import type { PeerGraphNode, PeerGraphLink } from "../api/types";

interface SimNode extends d3.SimulationNodeDatum, PeerGraphNode {}
interface SimLink extends d3.SimulationLinkDatum<SimNode> {
  type: string;
}

const NODE_COLORS: Record<string, string> = {
  center: "#c8a84e",
  torrent: "#8a9a3a",
  peer: "#5a8ab5",
};

const LINK_COLORS: Record<string, string> = {
  seeds: "#c8a84e",
  encrypted: "#8a9a3a",
  plain: "#5a5a5a",
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
  const [dimensions, setDimensions] = useState({ width: 800, height: 600 });

  const range = useMemo(() => getTimeRange(hours), [hours]);
  const {
    data: graphData,
    isLoading,
    isError,
  } = usePeerGraph(range.start, range.end);

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

  useEffect(() => {
    if (!graphData || !svgRef.current) return;

    const svg = d3.select(svgRef.current);
    svg.selectAll("*").remove();

    const { width, height } = dimensions;

    const nodes: SimNode[] = graphData.nodes.map((n) => ({ ...n }));
    const links: SimLink[] = graphData.links.map((l) => ({
      source: l.source,
      target: l.target,
      type: l.type,
    }));

    const centerNode = nodes.find((n) => n.type === "center");
    if (centerNode) {
      centerNode.fx = width / 2;
      centerNode.fy = height / 2;
    }

    const g = svg.append("g");

    const zoom = d3
      .zoom<SVGSVGElement, unknown>()
      .scaleExtent([0.1, 4])
      .on("zoom", (event) => {
        g.attr("transform", event.transform);
      });

    svg.call(zoom);

    const defs = svg.append("defs");
    defs
      .append("marker")
      .attr("id", "arrowhead")
      .attr("viewBox", "0 -5 10 10")
      .attr("refX", 20)
      .attr("refY", 0)
      .attr("markerWidth", 6)
      .attr("markerHeight", 6)
      .attr("orient", "auto")
      .append("path")
      .attr("d", "M0,-5L10,0L0,5")
      .attr("fill", "var(--text-dim)");

    const simulation = d3
      .forceSimulation<SimNode>(nodes)
      .force(
        "link",
        d3
          .forceLink<SimNode, SimLink>(links)
          .id((d) => d.id)
          .distance((d) => {
            const target = d.target as SimNode;
            return target?.type === "peer" ? 120 : 80;
          }),
      )
      .force(
        "charge",
        d3.forceManyBody().strength((d) => {
          if ((d as SimNode).type === "center") return -500;
          if ((d as SimNode).type === "torrent") return -300;
          return -100;
        }),
      )
      .force("center", d3.forceCenter(width / 2, height / 2))
      .force(
        "collision",
        d3.forceCollide().radius((d) => {
          if ((d as SimNode).type === "center") return 40;
          if ((d as SimNode).type === "torrent") return 25;
          return 15;
        }),
      );

    const link = g
      .append("g")
      .selectAll("line")
      .data(links)
      .join("line")
      .attr("stroke", (d) => LINK_COLORS[d.type] || "#555")
      .attr("stroke-opacity", 0.6)
      .attr("stroke-width", (d) => (d.type === "seeds" ? 2 : 1))
      .attr("stroke-dasharray", (d) => (d.type === "encrypted" ? "5,3" : null));

    const node = g
      .append("g")
      .selectAll<SVGGElement, SimNode>("g")
      .data(nodes)
      .join("g")
      .call(
        d3
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
          }),
      );

    node
      .append("circle")
      .attr("r", (d) => {
        if (d.type === "center") return 30;
        if (d.type === "torrent") return 18;
        return 10;
      })
      .attr("fill", (d) => NODE_COLORS[d.type] || "#666")
      .attr("stroke", "var(--bg-primary)")
      .attr("stroke-width", 2)
      .attr("opacity", 0.9);

    node
      .append("text")
      .text((d) => d.label)
      .attr("text-anchor", "middle")
      .attr("dy", (d) => {
        if (d.type === "center") return 45;
        if (d.type === "torrent") return 30;
        return 22;
      })
      .attr("fill", "var(--text-primary)")
      .attr("font-size", (d) => {
        if (d.type === "center") return "12px";
        if (d.type === "torrent") return "10px";
        return "8px";
      })
      .attr("font-family", "inherit");

    node
      .filter((d) => d.type === "center")
      .append("text")
      .text("⬢")
      .attr("text-anchor", "middle")
      .attr("dy", 6)
      .attr("fill", "var(--bg-primary)")
      .attr("font-size", "20px");

    node
      .filter((d) => d.type === "torrent")
      .append("text")
      .text("■")
      .attr("text-anchor", "middle")
      .attr("dy", 5)
      .attr("fill", "var(--bg-primary)")
      .attr("font-size", "12px");

    node
      .filter((d) => d.isEncrypted === true)
      .append("circle")
      .attr("cx", (d) => (d.type === "peer" ? 8 : 15))
      .attr("cy", (d) => (d.type === "peer" ? -8 : -15))
      .attr("r", 5)
      .attr("fill", "#8a9a3a");

    node.append("title").text((d) => {
      if (d.type === "center") return "Seedarr Instance";
      if (d.type === "torrent")
        return `Torrent: ${d.label}\n${d.infoHash || ""}`;
      return `Peer: ${d.label}${d.isEncrypted ? " (encrypted)" : ""}`;
    });

    simulation.on("tick", () => {
      link
        .attr("x1", (d) => (d.source as SimNode).x || 0)
        .attr("y1", (d) => (d.source as SimNode).y || 0)
        .attr("x2", (d) => (d.target as SimNode).x || 0)
        .attr("y2", (d) => (d.target as SimNode).y || 0);

      node.attr("transform", (d) => `translate(${d.x || 0},${d.y || 0})`);
    });

    return () => {
      simulation.stop();
    };
  }, [graphData, dimensions]);

  const torrentCount =
    graphData?.nodes.filter((n) => n.type === "torrent").length ?? 0;
  const peerCount =
    graphData?.nodes.filter((n) => n.type === "peer").length ?? 0;

  return (
    <div>
      <h1 className="page-heading">Peer Map</h1>
      <div className="peer-map-controls">
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
        <span className="peer-map-stats">
          {torrentCount} torrents, {peerCount} peers
        </span>
      </div>
      <div className="peer-map-legend">
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
              borderColor: LINK_COLORS.encrypted,
              borderStyle: "dashed",
            }}
          />
          Encrypted
        </span>
        <span className="peer-map-legend-item">
          <span
            className="peer-map-legend-line"
            style={{ borderColor: LINK_COLORS.plain }}
          />
          Plain
        </span>
      </div>
      <div ref={containerRef} className="peer-map-container">
        {isLoading && (
          <div className="peer-map-loading">Loading peer data...</div>
        )}
        {!isLoading && isError && (
          <p className="error">Failed to load peer data.</p>
        )}
        {!isLoading && !isError && peerCount === 0 && (
          <div className="peer-map-empty">
            No peer connections in the selected time range.
          </div>
        )}
        <svg
          ref={svgRef}
          width={dimensions.width}
          height={dimensions.height}
          className="peer-map-svg"
        />
      </div>
    </div>
  );
}

export default PeerMap;
