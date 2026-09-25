import React, { useState, useEffect, useCallback } from "react";
import { apiClient } from "../api/client";
import { DeveloperNav } from "../components/DeveloperNav";
import type { DeveloperSimulationResponse } from "../api/types";

function formatBytes(bytes: number, decimals = 2): string {
  if (bytes === 0) return "0 Bytes";
  const k = 1024;
  const dm = decimals < 0 ? 0 : decimals;
  const sizes = ["Bytes", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + " " + sizes[i];
}

export default function DeveloperSimulation() {
  const [data, setData] = useState<DeveloperSimulationResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: "success" | "error" } | null>(null);

  const fetchSimulation = useCallback(async () => {
    try {
      const res = await apiClient.get<DeveloperSimulationResponse>("/system/developer/simulation");
      setData(res);
    } catch {
      // background fetch
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchSimulation();
    const interval = setInterval(fetchSimulation, 3000);
    return () => clearInterval(interval);
  }, [fetchSimulation]);

  return (
    <div style={{ padding: "20px", maxWidth: "1600px", margin: "0 auto" }}>
      <DeveloperNav isSeedarr />

      {/* Top Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "12px",
          marginBottom: "16px",
        }}
      >
        <div>
          <h2 style={{ margin: "0 0 4px 0", fontSize: "1.4rem", fontWeight: 700 }}>
            🧪 Seedarr Swarm Simulation Lab
          </h2>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-secondary, #94a3b8)" }}>
            Inspect Swarm traffic simulation metrics, algorithm distribution, client emulation profiles, and MCP status.
          </p>
        </div>

        <div>
          <button
            onClick={() => {
              fetchSimulation();
              setActionMessage({ text: "Simulation metrics refreshed.", type: "success" });
            }}
            style={{
              padding: "7px 14px",
              borderRadius: "6px",
              border: "1px solid var(--border, #334155)",
              backgroundColor: "var(--bg-surface, #1e293b)",
              color: "#fff",
              cursor: "pointer",
              fontSize: "0.83rem",
              fontWeight: 600,
            }}
          >
            🔄 Refresh Metrics
          </button>
        </div>
      </div>

      {actionMessage && (
        <div
          style={{
            padding: "10px 14px",
            borderRadius: "6px",
            marginBottom: "12px",
            fontSize: "0.85rem",
            backgroundColor: actionMessage.type === "success" ? "rgba(16, 185, 129, 0.15)" : "rgba(239, 68, 68, 0.15)",
            border: `1px solid ${actionMessage.type === "success" ? "#10b981" : "#ef4444"}`,
            color: actionMessage.type === "success" ? "#34d399" : "#f87171",
          }}
        >
          {actionMessage.text}
        </div>
      )}

      {isLoading && !data ? (
        <div style={{ textAlign: "center", padding: "40px", color: "var(--text-secondary)" }}>
          Loading simulation lab diagnostics...
        </div>
      ) : data ? (
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          {/* Key Metric Cards */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "14px" }}>
            <div
              style={{
                backgroundColor: "var(--bg-surface, #1e293b)",
                border: "1px solid var(--border, #334155)",
                borderRadius: "8px",
                padding: "16px",
              }}
            >
              <div style={{ fontSize: "0.8rem", color: "var(--text-secondary)", marginBottom: "4px" }}>Simulation State</div>
              <div style={{ fontSize: "1.4rem", fontWeight: 700, color: data.isRunning ? "#34d399" : "#f87171" }}>
                {data.isRunning ? "Active" : "Stopped"}
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--text-secondary)", marginTop: "4px" }}>
                Swarm loop running
              </div>
            </div>

            <div
              style={{
                backgroundColor: "var(--bg-surface, #1e293b)",
                border: "1px solid var(--border, #334155)",
                borderRadius: "8px",
                padding: "16px",
              }}
            >
              <div style={{ fontSize: "0.8rem", color: "var(--text-secondary)", marginBottom: "4px" }}>Distribution Algorithm</div>
              <div style={{ fontSize: "1.3rem", fontWeight: 700, color: "#60a5fa" }}>
                {data.activeAlgorithm}
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--text-secondary)", marginTop: "4px" }}>
                Upload rate distributor
              </div>
            </div>

            <div
              style={{
                backgroundColor: "var(--bg-surface, #1e293b)",
                border: "1px solid var(--border, #334155)",
                borderRadius: "8px",
                padding: "16px",
              }}
            >
              <div style={{ fontSize: "0.8rem", color: "var(--text-secondary)", marginBottom: "4px" }}>Simulated Torrents</div>
              <div style={{ fontSize: "1.4rem", fontWeight: 700, color: "#f59e0b" }}>
                {data.activeSimulatedTorrents}
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--text-secondary)", marginTop: "4px" }}>
                Active in tracker swarm
              </div>
            </div>

            <div
              style={{
                backgroundColor: "var(--bg-surface, #1e293b)",
                border: "1px solid var(--border, #334155)",
                borderRadius: "8px",
                padding: "16px",
              }}
            >
              <div style={{ fontSize: "0.8rem", color: "var(--text-secondary)", marginBottom: "4px" }}>Current Rates</div>
              <div style={{ fontSize: "1.1rem", fontWeight: 700, color: "#a78bfa" }}>
                ▲ {formatBytes(data.currentUploadRateBytesPerSec)}/s
              </div>
              <div style={{ fontSize: "0.85rem", color: "var(--text-secondary)", marginTop: "2px" }}>
                ▼ {formatBytes(data.currentDownloadRateBytesPerSec)}/s
              </div>
            </div>
          </div>

          {/* Traffic Aggregates & Profiles */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
            <div
              style={{
                backgroundColor: "var(--bg-surface, #1e293b)",
                border: "1px solid var(--border, #334155)",
                borderRadius: "8px",
                padding: "16px",
              }}
            >
              <h3 style={{ margin: "0 0 12px 0", fontSize: "0.95rem", fontWeight: 600 }}>
                Cumulative Swarm Data Transfer
              </h3>
              <div style={{ display: "flex", flexDirection: "column", gap: "10px", fontSize: "0.85rem" }}>
                <div style={{ display: "flex", justifyContent: "space-between", padding: "8px 10px", backgroundColor: "var(--bg-primary)", borderRadius: "4px" }}>
                  <span>Total Uploaded (Simulated):</span>
                  <span style={{ fontWeight: 700, color: "#34d399", fontFamily: "monospace" }}>
                    {formatBytes(data.totalUploadedBytes)}
                  </span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", padding: "8px 10px", backgroundColor: "var(--bg-primary)", borderRadius: "4px" }}>
                  <span>Total Downloaded (Simulated):</span>
                  <span style={{ fontWeight: 700, color: "#60a5fa", fontFamily: "monospace" }}>
                    {formatBytes(data.totalDownloadedBytes)}
                  </span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", padding: "8px 10px", backgroundColor: "var(--bg-primary)", borderRadius: "4px" }}>
                  <span>Effective Seed Ratio:</span>
                  <span style={{ fontWeight: 700, color: "#fbbf24", fontFamily: "monospace" }}>
                    {data.totalDownloadedBytes > 0
                      ? (data.totalUploadedBytes / data.totalDownloadedBytes).toFixed(2)
                      : "∞"}
                  </span>
                </div>
              </div>
            </div>

            <div
              style={{
                backgroundColor: "var(--bg-surface, #1e293b)",
                border: "1px solid var(--border, #334155)",
                borderRadius: "8px",
                padding: "16px",
              }}
            >
              <h3 style={{ margin: "0 0 12px 0", fontSize: "0.95rem", fontWeight: 600 }}>
                Available Client Emulation Profiles
              </h3>
              <div style={{ display: "flex", flexWrap: "wrap", gap: "8px" }}>
                {data.clientProfiles.map((p) => (
                  <div
                    key={p}
                    style={{
                      padding: "6px 12px",
                      borderRadius: "6px",
                      border: "1px solid var(--border, #334155)",
                      backgroundColor: "var(--bg-primary, #0f172a)",
                      fontSize: "0.8rem",
                      fontWeight: 600,
                      color: "#e2e8f0",
                    }}
                  >
                    💻 {p}
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* MCP Protocol Diagnostics */}
          <div
            style={{
              backgroundColor: "var(--bg-surface, #1e293b)",
              border: "1px solid var(--border, #334155)",
              borderRadius: "8px",
              padding: "16px",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
              <h3 style={{ margin: 0, fontSize: "0.95rem", fontWeight: 600 }}>
                🤖 Model Context Protocol (MCP) Diagnostics
              </h3>
              <span
                style={{
                  padding: "3px 8px",
                  borderRadius: "4px",
                  fontSize: "0.75rem",
                  fontWeight: 700,
                  backgroundColor: data.mcpEnabled ? "rgba(16, 185, 129, 0.2)" : "rgba(148, 163, 184, 0.2)",
                  color: data.mcpEnabled ? "#34d399" : "#94a3b8",
                }}
              >
                {data.mcpEnabled ? "MCP Server Active" : "MCP Server Inactive"}
              </span>
            </div>

            <div style={{ fontSize: "0.82rem", color: "var(--text-secondary)", marginBottom: "12px" }}>
              The Seedarr MCP server provides AI coding agents and LLMs with tool execution capabilities over JSON-RPC.
            </div>

            <div style={{ display: "flex", flexWrap: "wrap", gap: "8px" }}>
              {data.mcpTools.map((tool) => (
                <div
                  key={tool}
                  style={{
                    padding: "4px 10px",
                    borderRadius: "4px",
                    backgroundColor: "rgba(59, 130, 246, 0.15)",
                    color: "#60a5fa",
                    fontFamily: "monospace",
                    fontSize: "0.78rem",
                  }}
                >
                  ⚡ {tool}
                </div>
              ))}
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
