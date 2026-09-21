import { Component } from "react";
import type { ErrorInfo, ReactNode } from "react";
import { trackException } from "../utils/analytics";

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
  errorInfo: ErrorInfo | null;
  copied: boolean;
}

class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = {
      hasError: false,
      error: null,
      errorInfo: null,
      copied: false,
    };
  }

  static getDerivedStateFromError(error: Error): Partial<State> {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error("ErrorBoundary caught an unhandled error:", error, errorInfo);
    this.setState({ errorInfo });
    try {
      trackException(error.message, false);
    } catch {
      // ignore telemetry errors
    }
  }

  handleReset = () => {
    this.setState({
      hasError: false,
      error: null,
      errorInfo: null,
      copied: false,
    });
  };

  handleReload = () => {
    window.location.reload();
  };

  handleCopyStack = () => {
    const errorDetails = [
      `Error: ${this.state.error?.name || "Error"}: ${this.state.error?.message || "Unknown error"}`,
      `Stack Trace:\n${this.state.error?.stack || "No stack trace available"}`,
      `Component Stack:\n${this.state.errorInfo?.componentStack || "No component stack available"}`,
    ].join("\n\n");

    navigator.clipboard
      .writeText(errorDetails)
      .then(() => {
        this.setState({ copied: true });
        setTimeout(() => this.setState({ copied: false }), 2500);
      })
      .catch((err) => {
        console.warn("Failed to copy error to clipboard:", err);
      });
  };

  render() {
    if (this.state.hasError) {
      return (
        <div
          className="error-boundary"
          role="alert"
          style={{
            minHeight: "400px",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            padding: "2rem",
          }}
        >
          <div
            className="error-boundary-content"
            style={{
              maxWidth: "680px",
              width: "100%",
              backgroundColor: "var(--bg-card, #161826)",
              borderRadius: "8px",
              padding: "2rem",
              border: "1px solid rgba(239, 68, 68, 0.3)",
              boxShadow: "0 8px 30px rgba(0, 0, 0, 0.5)",
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
                marginBottom: "1rem",
              }}
            >
              <span style={{ fontSize: "2rem" }}>⚠️</span>
              <div>
                <h2
                  className="error-boundary-title"
                  style={{
                    margin: 0,
                    fontSize: "1.3rem",
                    color: "var(--danger, #ef4444)",
                    fontWeight: 600,
                  }}
                >
                  Something went wrong
                </h2>
                <div
                  style={{
                    fontSize: "0.85rem",
                    color: "var(--text-muted, #94a3b8)",
                    marginTop: "0.2rem",
                  }}
                >
                  An unexpected error occurred while rendering this component.
                </div>
              </div>
            </div>

            <p
              className="error-boundary-message"
              style={{
                margin: "0 0 1.25rem 0",
                fontSize: "0.95rem",
                color: "var(--text-primary, #fff)",
                backgroundColor: "rgba(239, 68, 68, 0.1)",
                padding: "0.75rem 1rem",
                borderRadius: "6px",
                borderLeft: "4px solid var(--danger, #ef4444)",
                fontFamily: "monospace",
                wordBreak: "break-word",
              }}
            >
              {this.state.error?.message || "An unexpected error occurred."}
            </p>

            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
                marginBottom: "1.5rem",
                flexWrap: "wrap",
              }}
            >
              <button
                type="button"
                className="btn btn-primary"
                onClick={this.handleReset}
              >
                Try Again
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={this.handleReload}
              >
                Reload Page
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={this.handleCopyStack}
                style={{ marginLeft: "auto" }}
              >
                {this.state.copied ? "✓ Copied Details" : "📋 Copy Error Details"}
              </button>
            </div>

            {/* Collapsible Error Details and Stack Trace */}
            <details
              style={{
                border: "1px solid var(--border-light)",
                borderRadius: "6px",
                padding: "0.75rem",
                backgroundColor: "var(--bg-primary, #0f111c)",
                fontSize: "0.8rem",
              }}
            >
              <summary
                style={{
                  cursor: "pointer",
                  fontWeight: 600,
                  color: "var(--text-secondary, #cbd5e1)",
                  userSelect: "none",
                  outline: "none",
                }}
              >
                Show Technical Error Details & Stack Trace
              </summary>
              <div
                style={{
                  marginTop: "0.75rem",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.5rem",
                }}
              >
                {this.state.error?.stack && (
                  <div>
                    <div
                      style={{
                        fontWeight: 600,
                        color: "var(--text-muted, #94a3b8)",
                        marginBottom: "0.25rem",
                      }}
                    >
                      Error Stack:
                    </div>
                    <pre
                      style={{
                        margin: 0,
                        padding: "0.5rem",
                        backgroundColor: "rgba(0, 0, 0, 0.4)",
                        borderRadius: "4px",
                        overflowX: "auto",
                        fontSize: "0.75rem",
                        color: "var(--danger, #f87171)",
                        lineHeight: 1.4,
                      }}
                    >
                      {this.state.error.stack}
                    </pre>
                  </div>
                )}
                {this.state.errorInfo?.componentStack && (
                  <div>
                    <div
                      style={{
                        fontWeight: 600,
                        color: "var(--text-muted, #94a3b8)",
                        marginBottom: "0.25rem",
                      }}
                    >
                      Component Stack:
                    </div>
                    <pre
                      style={{
                        margin: 0,
                        padding: "0.5rem",
                        backgroundColor: "rgba(0, 0, 0, 0.4)",
                        borderRadius: "4px",
                        overflowX: "auto",
                        fontSize: "0.75rem",
                        color: "var(--text-muted, #94a3b8)",
                        lineHeight: 1.4,
                      }}
                    >
                      {this.state.errorInfo.componentStack}
                    </pre>
                  </div>
                )}
              </div>
            </details>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}

export { ErrorBoundary };
export default ErrorBoundary;
