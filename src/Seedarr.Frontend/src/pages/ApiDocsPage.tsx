import { useState } from "react";
import { useGeneralConfig } from "../api/hooks";
import { useToast } from "../context/ToastContext";

function ApiDocsPage() {
  const { data: generalConfig } = useGeneralConfig();
  const { showToast } = useToast();
  const [copied, setCopied] = useState(false);

  const handleCopyKey = () => {
    if (generalConfig?.apiKey) {
      navigator.clipboard.writeText(generalConfig.apiKey);
      setCopied(true);
      showToast("API Key copied to clipboard!", "success");
      setTimeout(() => setCopied(false), 2000);
    } else {
      showToast("No API Key available", "error");
    }
  };

  const handleOpenJson = () => {
    window.open("/swagger/v1/swagger.json", "_blank", "noopener,noreferrer");
  };

  const handleOpenFullPage = () => {
    window.open("/swagger/index.html", "_blank", "noopener,noreferrer");
  };

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "calc(100vh - 110px)",
        minHeight: "650px",
      }}
    >
      {/* Page Header */}
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.25rem",
          flexShrink: 0,
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              System: API Reference
            </h1>
            <span className="badge badge-primary">OpenAPI v3</span>
          </div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.2rem",
            }}
          >
            Interactive REST API explorer, parameter definitions, and endpoint
            schemas
          </div>
        </div>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          <button
            className="btn btn-outline"
            onClick={handleCopyKey}
            title="Copy API Key to clipboard"
            style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
          >
            <span>📋</span>
            <span>{copied ? "Copied!" : "Copy API Key"}</span>
          </button>
          <button
            className="btn btn-outline"
            onClick={handleOpenJson}
            title="View Raw OpenAPI Specification (JSON)"
            style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
          >
            <span>📥</span>
            <span>OpenAPI JSON</span>
          </button>
          <button
            className="btn btn-primary"
            onClick={handleOpenFullPage}
            title="Open Swagger UI in a standalone tab"
            style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
          >
            <span>↗</span>
            <span>Open Full Page</span>
          </button>
        </div>
      </div>

      {/* Embedded Swagger UI Frame */}
      <div
        className="card"
        style={{
          flex: 1,
          padding: 0,
          overflow: "hidden",
          borderRadius: "8px",
          border: "1px solid rgba(255, 255, 255, 0.08)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          display: "flex",
          flexDirection: "column",
        }}
      >
        <iframe
          src="/swagger/index.html"
          title="Seedarr REST API Documentation"
          style={{
            width: "100%",
            height: "100%",
            border: "none",
            flex: 1,
            backgroundColor: "var(--bg-primary, #222018)",
          }}
        />
      </div>
    </div>
  );
}

export default ApiDocsPage;
