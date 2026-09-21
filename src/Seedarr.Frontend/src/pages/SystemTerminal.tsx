import {
  useState,
  useEffect,
  useRef,
  useCallback,
  useMemo,
  type KeyboardEvent,
  type MouseEvent,
} from "react";
import {
  HubConnectionBuilder,
  type HubConnection,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { usePermissions } from "../hooks/usePermissions";
import { useGeneralConfig } from "../api/hooks";
import { getAccessToken } from "../api/signalr";
import { copyToClipboard } from "../utils/clipboard";
import { useTranslation } from "../i18n";

export type TerminalConnectionStatus =
  | "Connected"
  | "Reconnecting..."
  | "Disconnected";

export interface AnsiSpan {
  text: string;
  color?: string;
  backgroundColor?: string;
  bold?: boolean;
  dim?: boolean;
  italic?: boolean;
  underline?: boolean;
}

const ANSI_COLORS: Record<number, string> = {
  30: "#1e1e1e",
  31: "#ef4444",
  32: "#22c55e",
  33: "#eab308",
  34: "#3b82f6",
  35: "#a855f7",
  36: "#06b6d4",
  37: "#e5e7eb",
  90: "#6b7280",
  91: "#f87171",
  92: "#4ade80",
  93: "#fde047",
  94: "#60a5fa",
  95: "#c084fc",
  96: "#22d3ee",
  97: "#ffffff",
};

const ANSI_BG_COLORS: Record<number, string> = {
  40: "#000000",
  41: "#7f1d1d",
  42: "#14532d",
  43: "#713f12",
  44: "#1e3a8a",
  45: "#581c87",
  46: "#164e63",
  47: "#d1d5db",
  100: "#374151",
  101: "#991b1b",
  102: "#166534",
  103: "#854d0e",
  104: "#1e40af",
  105: "#6b21a8",
  106: "#155e75",
  107: "#ffffff",
};

const ANSI_256_TABLE: string[] = (() => {
  const table: string[] = [
    "#000000", "#ef4444", "#22c55e", "#eab308", "#3b82f6", "#a855f7", "#06b6d4", "#e5e7eb",
    "#6b7280", "#f87171", "#4ade80", "#fde047", "#60a5fa", "#c084fc", "#22d3ee", "#ffffff",
  ];
  const steps = [0, 95, 135, 175, 215, 255];
  for (let r = 0; r < 6; r++) {
    for (let g = 0; g < 6; g++) {
      for (let b = 0; b < 6; b++) {
        table.push(`rgb(${steps[r]},${steps[g]},${steps[b]})`);
      }
    }
  }
  for (let i = 0; i < 24; i++) {
    const v = 8 + i * 10;
    table.push(`rgb(${v},${v},${v})`);
  }
  return table;
})();

export function parseAnsiLine(rawText: string): AnsiSpan[] {
  // Strip OSC escape sequences (e.g. \x1b]0;title\x07)
  const textWithoutOsc = rawText.replace(/\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)/g, "");

  const spans: AnsiSpan[] = [];
  let currentColor: string | undefined = undefined;
  let currentBgColor: string | undefined = undefined;
  let isBold = false;
  let isDim = false;
  let isItalic = false;
  let isUnderline = false;

  const csiRegex = /\x1b\[([0-9;?]*)?([a-zA-Z])/g;
  let lastIndex = 0;
  let match: RegExpExecArray | null;

  while ((match = csiRegex.exec(textWithoutOsc)) !== null) {
    const textChunk = textWithoutOsc.slice(lastIndex, match.index);
    if (textChunk.length > 0) {
      spans.push({
        text: textChunk,
        color: currentColor,
        backgroundColor: currentBgColor,
        bold: isBold,
        dim: isDim,
        italic: isItalic,
        underline: isUnderline,
      });
    }

    lastIndex = match.index + match[0].length;
    const params = match[1] || "";
    const command = match[2];

    if (command === "m") {
      const codes = params
        ? params.split(";").map((p) => parseInt(p, 10)).filter((p) => !isNaN(p))
        : [0];

      if (codes.length === 0) {
        currentColor = undefined;
        currentBgColor = undefined;
        isBold = false;
        isDim = false;
        isItalic = false;
        isUnderline = false;
      } else {
        for (let i = 0; i < codes.length; i++) {
          const code = codes[i];
          if (code === 0) {
            currentColor = undefined;
            currentBgColor = undefined;
            isBold = false;
            isDim = false;
            isItalic = false;
            isUnderline = false;
          } else if (code === 1) {
            isBold = true;
          } else if (code === 2) {
            isDim = true;
          } else if (code === 3) {
            isItalic = true;
          } else if (code === 4) {
            isUnderline = true;
          } else if (code === 22) {
            isBold = false;
            isDim = false;
          } else if (code === 23) {
            isItalic = false;
          } else if (code === 24) {
            isUnderline = false;
          } else if (code === 39) {
            currentColor = undefined;
          } else if (code === 49) {
            currentBgColor = undefined;
          } else if (ANSI_COLORS[code]) {
            currentColor = ANSI_COLORS[code];
          } else if (ANSI_BG_COLORS[code]) {
            currentBgColor = ANSI_BG_COLORS[code];
          } else if (code === 38 && codes[i + 1] === 5 && codes[i + 2] !== undefined) {
            const idx = codes[i + 2];
            currentColor = ANSI_256_TABLE[idx] || `color-${idx}`;
            i += 2;
          } else if (code === 38 && codes[i + 1] === 2 && codes[i + 4] !== undefined) {
            currentColor = `rgb(${codes[i + 2]},${codes[i + 3]},${codes[i + 4]})`;
            i += 4;
          } else if (code === 48 && codes[i + 1] === 5 && codes[i + 2] !== undefined) {
            const idx = codes[i + 2];
            currentBgColor = ANSI_256_TABLE[idx] || `color-${idx}`;
            i += 2;
          } else if (code === 48 && codes[i + 1] === 2 && codes[i + 4] !== undefined) {
            currentBgColor = `rgb(${codes[i + 2]},${codes[i + 3]},${codes[i + 4]})`;
            i += 4;
          }
        }
      }
    }
  }

  const remaining = textWithoutOsc.slice(lastIndex);
  if (remaining.length > 0) {
    spans.push({
      text: remaining,
      color: currentColor,
      backgroundColor: currentBgColor,
      bold: isBold,
      dim: isDim,
      italic: isItalic,
      underline: isUnderline,
    });
  }

  return spans;
}

function AnsiLineRenderer({ line }: { line: string }) {
  const spans = useMemo(() => parseAnsiLine(line), [line]);
  if (spans.length === 0) {
    return <div className="terminal-line">&nbsp;</div>;
  }
  return (
    <div className="terminal-line">
      {spans.map((span, idx) => (
        <span
          key={idx}
          style={{
            color: span.color,
            backgroundColor: span.backgroundColor,
            fontWeight: span.bold ? 700 : span.dim ? 300 : undefined,
            fontStyle: span.italic ? "italic" : undefined,
            textDecoration: span.underline ? "underline" : undefined,
            opacity: span.dim ? 0.75 : 1,
          }}
        >
          {span.text}
        </span>
      ))}
    </div>
  );
}

export function SystemTerminal() {
  const { t } = useTranslation();
  const { isAdmin } = usePermissions();
  const { data: config } = useGeneralConfig();
  const terminalAccessEnabled = config?.terminalAccessEnabled ?? true;

  const [connectionStatus, setConnectionStatus] =
    useState<TerminalConnectionStatus>("Disconnected");
  const [lines, setLines] = useState<string[]>([]);
  const [inputValue, setInputValue] = useState("");
  const [commandHistory, setCommandHistory] = useState<string[]>([]);
  const [historyIndex, setHistoryIndex] = useState<number>(-1);
  const historyDraftRef = useRef<string>("");

  const [cols, setCols] = useState(80);
  const [rows, setRows] = useState(24);
  const colsRef = useRef(80);
  const rowsRef = useRef(24);
  colsRef.current = cols;
  rowsRef.current = rows;

  const connectionRef = useRef<HubConnection | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const terminalBodyRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const measureRef = useRef<HTMLSpanElement | null>(null);

  const isAccessAllowed = isAdmin && terminalAccessEnabled;

  // Append stream output to lines state
  const appendTerminalOutput = useCallback((incomingText: string) => {
    if (!incomingText) return;
    setLines((prev) => {
      const normalized = incomingText.replace(/\r\n/g, "\n");
      const segments = normalized.split("\n");

      let updated = [...prev];
      if (updated.length === 0) {
        updated = [""];
      }

      updated[updated.length - 1] += segments[0];

      for (let i = 1; i < segments.length; i++) {
        updated.push(segments[i]);
      }

      // Handle standalone carriage returns (\r) overwriting line content
      updated = updated.map((l) => {
        if (l.includes("\r")) {
          const rParts = l.split("\r");
          return rParts[rParts.length - 1];
        }
        return l;
      });

      // Keep maximum 2000 lines
      if (updated.length > 2000) {
        return updated.slice(updated.length - 2000);
      }
      return updated;
    });
  }, []);

  // Transmit input / command to hub
  const transmitInput = useCallback(async (data: string) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) {
      return;
    }
    try {
      await conn.invoke("WriteInput", data);
    } catch {
      try {
        await conn.invoke("SendInput", data);
      } catch {
        try {
          await conn.invoke("ExecuteCommand", data);
        } catch (err) {
          console.error("Failed to transmit input to hub:", err);
        }
      }
    }
  }, []);

  // Connect to SignalR Terminal Hub
  const connectTerminal = useCallback(() => {
    if (!isAccessAllowed) {
      setConnectionStatus("Disconnected");
      return;
    }

    if (connectionRef.current) {
      try {
        connectionRef.current.stop();
      } catch {
        // ignore
      }
      connectionRef.current = null;
    }

    setConnectionStatus("Reconnecting...");

    const conn = new HubConnectionBuilder()
      .withUrl("/signalr/terminal", {
        accessTokenFactory: () => getAccessToken(),
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    conn.onreconnecting(() => setConnectionStatus("Reconnecting..."));

    conn.onreconnected(async () => {
      setConnectionStatus("Connected");
      try {
        await conn.invoke("StartSession", colsRef.current, rowsRef.current);
      } catch {
        // ignore
      }
    });

    conn.onclose(() => setConnectionStatus("Disconnected"));

    // Listen for terminal output from hub (TerminalHub sends ReceiveOutput; specs also mention Output and Data)
    const handleIncoming = (output: string) => appendTerminalOutput(output);
    conn.on("ReceiveOutput", handleIncoming);
    conn.on("Output", handleIncoming);
    conn.on("Data", handleIncoming);

    conn
      .start()
      .then(async () => {
        setConnectionStatus("Connected");
        try {
          await conn.invoke("StartSession", colsRef.current, rowsRef.current);
        } catch {
          // ignore
        }
      })
      .catch((err) => {
        console.error("Terminal SignalR connection failed:", err);
        setConnectionStatus("Disconnected");
      });

    connectionRef.current = conn;
  }, [isAccessAllowed, appendTerminalOutput]);

  // Establish connection on mount or access change
  useEffect(() => {
    if (isAccessAllowed) {
      connectTerminal();
    } else {
      setConnectionStatus("Disconnected");
      if (connectionRef.current) {
        connectionRef.current.stop().catch(() => {});
        connectionRef.current = null;
      }
    }

    return () => {
      if (connectionRef.current) {
        connectionRef.current.stop().catch(() => {});
        connectionRef.current = null;
      }
    };
  }, [isAccessAllowed, connectTerminal]);

  // Container auto-fit resizing with ResizeObserver
  useEffect(() => {
    if (!containerRef.current || typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const { width, height } = entry.contentRect;
        if (width <= 0 || height <= 0) continue;

        let charW = 8.5;
        let charH = 18;
        if (measureRef.current) {
          const rect = measureRef.current.getBoundingClientRect();
          if (rect.width > 0) charW = rect.width;
          if (rect.height > 0) charH = rect.height;
        }

        const calculatedCols = Math.max(
          20,
          Math.min(250, Math.floor((width - 32) / charW)),
        );
        const calculatedRows = Math.max(
          5,
          Math.min(100, Math.floor((height - 110) / charH)),
        );

        setCols(calculatedCols);
        setRows(calculatedRows);

        const conn = connectionRef.current;
        if (conn && conn.state === HubConnectionState.Connected) {
          conn.invoke("Resize", calculatedCols, calculatedRows).catch(() => {});
        }
      }
    });

    observer.observe(containerRef.current);
    return () => observer.disconnect();
  }, []);

  // Auto-scroll to bottom of terminal output
  useEffect(() => {
    if (terminalBodyRef.current) {
      terminalBodyRef.current.scrollTop = terminalBodyRef.current.scrollHeight;
    }
  }, [lines]);

  // Command input handlers
  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    // Ctrl+L to clear screen
    if (e.ctrlKey && (e.key === "l" || e.key === "L")) {
      e.preventDefault();
      setLines([]);
      transmitInput("\x0c");
      return;
    }

    // Ctrl+C to send interrupt / break (unless text is selected in the window)
    if (e.ctrlKey && (e.key === "c" || e.key === "C")) {
      const selection = window.getSelection()?.toString();
      if (!selection || selection.length === 0) {
        e.preventDefault();
        transmitInput("\x03");
        return;
      }
    }

    // Up Arrow: History navigation backwards
    if (e.key === "ArrowUp") {
      e.preventDefault();
      if (commandHistory.length === 0) return;
      if (historyIndex === -1) {
        historyDraftRef.current = inputValue;
        const nextIdx = commandHistory.length - 1;
        setHistoryIndex(nextIdx);
        setInputValue(commandHistory[nextIdx]);
      } else if (historyIndex > 0) {
        const nextIdx = historyIndex - 1;
        setHistoryIndex(nextIdx);
        setInputValue(commandHistory[nextIdx]);
      }
      return;
    }

    // Down Arrow: History navigation forwards
    if (e.key === "ArrowDown") {
      e.preventDefault();
      if (historyIndex === -1) return;
      if (historyIndex < commandHistory.length - 1) {
        const nextIdx = historyIndex + 1;
        setHistoryIndex(nextIdx);
        setInputValue(commandHistory[nextIdx]);
      } else {
        setHistoryIndex(-1);
        setInputValue(historyDraftRef.current);
      }
      return;
    }

    // Enter: Submit command
    if (e.key === "Enter") {
      e.preventDefault();
      const cmd = inputValue;
      if (cmd.trim().length > 0) {
        setCommandHistory((prev) => {
          if (prev[prev.length - 1] === cmd) return prev;
          return [...prev, cmd];
        });
      }
      setHistoryIndex(-1);
      historyDraftRef.current = "";
      setInputValue("");
      transmitInput(cmd + "\n");
    }
  };

  // Keyboard shortcut listener on container
  const handleContainerKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.ctrlKey && (e.key === "l" || e.key === "L")) {
      e.preventDefault();
      setLines([]);
      transmitInput("\x0c");
      inputRef.current?.focus();
    } else if (e.ctrlKey && (e.key === "c" || e.key === "C")) {
      const selection = window.getSelection()?.toString();
      if (!selection || selection.length === 0) {
        e.preventDefault();
        transmitInput("\x03");
        inputRef.current?.focus();
      }
    }
  };

  const handleClear = () => {
    setLines([]);
    transmitInput("\x0c");
    inputRef.current?.focus();
  };

  const handleCopy = async () => {
    const selection = window.getSelection()?.toString();
    const textToCopy =
      selection && selection.length > 0 ? selection : lines.join("\n");
    if (textToCopy) {
      await copyToClipboard(textToCopy);
    }
  };

  const handlePaste = async () => {
    if (typeof navigator !== "undefined" && navigator.clipboard?.readText) {
      try {
        const text = await navigator.clipboard.readText();
        if (text) {
          setInputValue((prev) => prev + text);
          inputRef.current?.focus();
        }
      } catch {
        // Fallback: clipboard permission denied
      }
    }
  };

  const handleTerminalBodyClick = (e: MouseEvent<HTMLDivElement>) => {
    const selection = window.getSelection()?.toString();
    if (!selection || selection.length === 0) {
      inputRef.current?.focus();
    }
  };

  return (
    <div className="system-terminal-page" style={{ padding: "1.5rem" }}>
      {/* Hidden element for measuring character dimensions */}
      <span
        ref={measureRef}
        aria-hidden="true"
        style={{
          position: "absolute",
          visibility: "hidden",
          pointerEvents: "none",
          fontFamily:
            'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace',
          fontSize: "0.875rem",
          lineHeight: "1.45",
        }}
      >
        M
      </span>

      <div
        style={{
          marginBottom: "1rem",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "flex-start",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              margin: 0,
              fontSize: "1.5rem",
              fontWeight: 700,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>📟</span> {t("terminal.title", undefined, "Terminal")}
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            {t(
              "terminal.subtitle",
              undefined,
              "Interactive web console with PTY session management",
            )}
          </p>
        </div>
      </div>

      {/* Permission Warning Banner */}
      {!isAdmin && (
        <div
          role="alert"
          className="health-alert health-alert-warning"
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            padding: "0.75rem 1rem",
            borderRadius: "6px",
            marginBottom: "1rem",
          }}
        >
          <span>⚠️</span>
          <span>
            {t(
              "terminal.adminRequired",
              undefined,
              "Administrator privileges required for terminal access",
            )}
          </span>
        </div>
      )}

      {/* Disabled in Security Settings Warning Banner */}
      {isAdmin && !terminalAccessEnabled && (
        <div
          role="alert"
          className="health-alert health-alert-warning"
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            padding: "0.75rem 1rem",
            borderRadius: "6px",
            marginBottom: "1rem",
          }}
        >
          <span>⚠️</span>
          <span>
            {t(
              "terminal.accessDisabled",
              undefined,
              "Terminal access is disabled in Security settings",
            )}
          </span>
        </div>
      )}

      {/* Terminal Main Container */}
      <div
        ref={containerRef}
        className="terminal-container"
        tabIndex={0}
        onKeyDown={handleContainerKeyDown}
        aria-label="Web Terminal"
      >
        {/* Terminal Header */}
        <div className="terminal-header">
          <div className="terminal-header-title">
            <span>🖥️</span>
            <span>Console</span>
            <span
              className="terminal-dim-badge"
              title={t("terminal.dimensions", undefined, "Dimensions")}
            >
              {cols}x{rows}
            </span>
          </div>

          <div className="terminal-header-actions">
            {/* Connection Status Badge */}
            <span
              className={`terminal-status-badge terminal-status-${connectionStatus.toLowerCase().replace(/[^a-z]/g, "")}`}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.375rem",
                padding: "0.2rem 0.6rem",
                borderRadius: "9999px",
                fontSize: "0.75rem",
                fontWeight: 600,
                backgroundColor:
                  connectionStatus === "Connected"
                    ? "rgba(34, 197, 94, 0.15)"
                    : connectionStatus === "Reconnecting..."
                    ? "rgba(245, 158, 11, 0.15)"
                    : "rgba(239, 68, 68, 0.15)",
                color:
                  connectionStatus === "Connected"
                    ? "#22c55e"
                    : connectionStatus === "Reconnecting..."
                    ? "#f59e0b"
                    : "#ef4444",
                border: `1px solid ${
                  connectionStatus === "Connected"
                    ? "rgba(34, 197, 94, 0.3)"
                    : connectionStatus === "Reconnecting..."
                    ? "rgba(245, 158, 11, 0.3)"
                    : "rgba(239, 68, 68, 0.3)"
                }`,
              }}
            >
              <span
                style={{
                  width: "6px",
                  height: "6px",
                  borderRadius: "50%",
                  backgroundColor: "currentColor",
                }}
              />
              {connectionStatus === "Connected"
                ? t("terminal.connected", undefined, "Connected")
                : connectionStatus === "Reconnecting..."
                ? t("terminal.reconnecting", undefined, "Reconnecting...")
                : t("terminal.disconnected", undefined, "Disconnected")}
            </span>

            {/* Manual Reconnect Button */}
            {connectionStatus === "Disconnected" && isAccessAllowed && (
              <button
                type="button"
                className="btn btn-small btn-primary"
                onClick={connectTerminal}
              >
                {t("terminal.reconnect", undefined, "Reconnect")}
              </button>
            )}

            {/* Copy Button */}
            <button
              type="button"
              className="btn btn-small btn-outline"
              onClick={handleCopy}
              title="Copy selection or output"
            >
              {t("terminal.copy", undefined, "Copy")}
            </button>

            {/* Paste Button */}
            <button
              type="button"
              className="btn btn-small btn-outline"
              onClick={handlePaste}
              title="Paste from clipboard"
              disabled={!isAccessAllowed || connectionStatus !== "Connected"}
            >
              {t("terminal.paste", undefined, "Paste")}
            </button>

            {/* Clear Screen Button */}
            <button
              type="button"
              className="btn btn-small btn-outline"
              onClick={handleClear}
              title="Clear terminal buffer (Ctrl+L)"
            >
              {t("terminal.clear", undefined, "Clear")}
            </button>
          </div>
        </div>

        {/* Terminal Body with rendered output */}
        <div
          ref={terminalBodyRef}
          className="terminal-body"
          onClick={handleTerminalBodyClick}
        >
          {lines.map((line, idx) => (
            <AnsiLineRenderer key={idx} line={line} />
          ))}
        </div>

        {/* Command Input Prompt Row */}
        <div className="terminal-prompt-row">
          <span className="terminal-prompt-prefix">seedarr:~$</span>
          <input
            ref={inputRef}
            type="text"
            className="terminal-prompt-input"
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            onKeyDown={handleKeyDown}
            disabled={!isAccessAllowed || connectionStatus !== "Connected"}
            autoComplete="off"
            autoCorrect="off"
            autoCapitalize="off"
            spellCheck={false}
            placeholder={
              !isAdmin
                ? t(
                    "terminal.adminRequired",
                    undefined,
                    "Administrator privileges required for terminal access",
                  )
                : !terminalAccessEnabled
                ? t(
                    "terminal.accessDisabled",
                    undefined,
                    "Terminal access is disabled in Security settings",
                  )
                : t(
                    "terminal.inputPlaceholder",
                    undefined,
                    "Enter command...",
                  )
            }
          />
        </div>

        {/* Terminal Footer Hints */}
        <div className="terminal-footer-hints">
          <span>
            {t(
              "terminal.shortcutsHint",
              undefined,
              "Ctrl+L to clear, Ctrl+C to interrupt, Up/Down for command history",
            )}
          </span>
          <span>
            {cols} cols × {rows} rows
          </span>
        </div>
      </div>
    </div>
  );
}

export default SystemTerminal;
