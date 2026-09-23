import { useState, useEffect, useRef, useCallback } from "react";
import { useGeneralConfig } from "../api/hooks";
import { apiClient } from "../api/client";
import { useToast } from "../context/ToastContext";

interface SwaggerWindow extends Window {
  ui?: {
    preauthorizeApiKey?: (name: string, value: string) => void;
  };
}

const copyToClipboard = async (text: string): Promise<boolean> => {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Fallback below
    }
  }

  // Fallback for non-secure HTTP contexts
  try {
    const textArea = document.createElement("textarea");
    textArea.value = text;
    textArea.style.position = "fixed";
    textArea.style.opacity = "0";
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();
    const success = document.execCommand("copy");
    document.body.removeChild(textArea);
    return success;
  } catch {
    return false;
  }
};

function ApiDocsPage() {
  const { data: generalConfig } = useGeneralConfig();
  const { showToast } = useToast();
  const [copied, setCopied] = useState(false);
  const iframeRef = useRef<HTMLIFrameElement>(null);

  const injectApiKeyToSwagger = useCallback((key: string) => {
    try {
      const cw = iframeRef.current?.contentWindow as SwaggerWindow | null;
      if (!cw) return;
      const tryAuthorize = () => {
        if (cw.ui?.preauthorizeApiKey) {
          cw.ui.preauthorizeApiKey("ApiKeyHeader", key);
          cw.ui.preauthorizeApiKey("ApiKeyQuery", key);
          return true;
        }
        return false;
      };
      if (!tryAuthorize()) {
        const interval = setInterval(() => {
          if (tryAuthorize() || !iframeRef.current) {
            clearInterval(interval);
          }
        }, 200);
        setTimeout(() => clearInterval(interval), 5000);
      }
    } catch {
      // Ignore cross-origin or load timing errors
    }
  }, []);

  const getResolvedApiKey = useCallback(async (): Promise<string | null> => {
    try {
      let key = generalConfig?.apiKey;
      if (!key || key.includes("*")) {
        const res = await apiClient.getApiKey();
        key = res.apiKey;
      }
      return key && !key.includes("*") ? key : null;
    } catch {
      return null;
    }
  }, [generalConfig?.apiKey]);

  useEffect(() => {
    let isMounted = true;
    getResolvedApiKey().then((key) => {
      if (isMounted && key) {
        injectApiKeyToSwagger(key);
      }
    });
    return () => {
      isMounted = false;
    };
  }, [getResolvedApiKey, injectApiKeyToSwagger]);

  const handleCopyKey = async () => {
    try {
      const key = await getResolvedApiKey();
      if (key) {
        const success = await copyToClipboard(key);
        if (success) {
          injectApiKeyToSwagger(key);
          setCopied(true);
          showToast("API Key copied to clipboard!", "success");
          setTimeout(() => setCopied(false), 2000);
        } else {
          showToast("Failed to copy API Key", "error");
        }
      } else {
        showToast("No API Key available", "error");
      }
    } catch {
      showToast("Failed to copy API Key", "error");
    }
  };

  const handleOpenJson = () => {
    window.open("/swagger/v1/swagger.json", "_blank", "noopener,noreferrer");
  };

  const handleOpenFullPage = () => {
    window.open("/swagger/index.html", "_blank", "noopener,noreferrer");
  };

  const handleIframeLoad = async () => {
    const key = await getResolvedApiKey();
    if (key) {
      injectApiKeyToSwagger(key);
    }
  };

  return (
    <div
      className="content-area"
      style={{
        display: "flex",
        flexDirection: "column",
        height: "calc(100vh - 110px)",
        minHeight: "650px",
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
            <span>📖</span> System: API Reference
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Interactive REST API explorer, parameter definitions, and endpoint
            schemas
          </p>
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
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
          border: "1px solid var(--border-light)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          display: "flex",
          flexDirection: "column",
        }}
      >
        <iframe
          ref={iframeRef}
          src="/swagger/index.html"
          title="Seedarr REST API Documentation"
          onLoad={handleIframeLoad}
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
