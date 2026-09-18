import { useState, useRef, useMemo, useCallback, useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../api/client";

type LogLevel = "Trace" | "Debug" | "Info" | "Warn" | "Error";

interface ApiLogEntry {
  id: number;
  time: string;
  level: string;
  logger: string;
  message: string;
  exception: string | null;
}

interface EventEntry {
  id: number;
  timestamp: string;
  level: LogLevel;
  component: string;
  message: string;
  exception: string | null;
}

const ALL_LEVELS: LogLevel[] = ["Trace", "Debug", "Info", "Warn", "Error"];

const LEVEL_FILTER_OPTIONS = [
  "All",
  "Error",
  "Warn",
  "Info",
  "Debug",
  "Trace",
] as const;
type LevelFilter = (typeof LEVEL_FILTER_OPTIONS)[number];

const PAGE_SIZE_OPTIONS = [25, 50, 100, 250] as const;

function toLogLevel(level: string): LogLevel {
  const normalized =
    level.charAt(0).toUpperCase() + level.slice(1).toLowerCase();
  if (ALL_LEVELS.includes(normalized as LogLevel)) {
    return normalized as LogLevel;
  }
  return "Info";
}

function useEventEntries() {
  return useQuery<EventEntry[]>({
    queryKey: ["system", "events"],
    queryFn: async () => {
      const data = await apiClient.get<ApiLogEntry[]>("/log?count=1000");
      return data.map((entry) => ({
        id: entry.id,
        timestamp: entry.time,
        level: toLogLevel(entry.level),
        component: entry.logger || "System",
        message: entry.message || "",
        exception: entry.exception || null,
      }));
    },
    refetchInterval: 10000,
  });
}

function formatEventTime(iso: string): string {
  if (!iso) return "-";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;

  const now = new Date();
  const isToday =
    d.getDate() === now.getDate() &&
    d.getMonth() === now.getMonth() &&
    d.getFullYear() === now.getFullYear();

  const yesterday = new Date(now);
  yesterday.setDate(yesterday.getDate() - 1);
  const isYesterday =
    d.getDate() === yesterday.getDate() &&
    d.getMonth() === yesterday.getMonth() &&
    d.getFullYear() === yesterday.getFullYear();

  let hours = d.getHours();
  const minutes = d.getMinutes().toString().padStart(2, "0");
  const seconds = d.getSeconds().toString().padStart(2, "0");
  const ampm = hours >= 12 ? "pm" : "am";
  hours = hours % 12;
  if (hours === 0) hours = 12;
  const timeStr = `${hours}:${minutes}:${seconds} ${ampm}`;

  if (isToday) {
    return `Today, ${timeStr}`;
  }
  if (isYesterday) {
    return `Yesterday, ${timeStr}`;
  }

  const pad = (n: number) => n.toString().padStart(2, "0");
  const year = d.getFullYear();
  const month = pad(d.getMonth() + 1);
  const day = pad(d.getDate());

  return `${year}-${month}-${day} ${timeStr}`;
}

function getPageNumbers(
  currentPage: number,
  totalPages: number,
): (number | string)[] {
  if (totalPages <= 7) {
    return Array.from({ length: totalPages }, (_, i) => i + 1);
  }
  if (currentPage <= 4) {
    return [1, 2, 3, 4, 5, "...", totalPages];
  }
  if (currentPage >= totalPages - 3) {
    return [
      1,
      "...",
      totalPages - 4,
      totalPages - 3,
      totalPages - 2,
      totalPages - 1,
      totalPages,
    ];
  }
  return [
    1,
    "...",
    currentPage - 1,
    currentPage,
    currentPage + 1,
    "...",
    totalPages,
  ];
}

function RefreshIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="23 4 23 10 17 10" />
      <polyline points="1 20 1 14 7 14" />
      <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
    </svg>
  );
}

function ClearIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </svg>
  );
}

function SortArrow({ direction }: { direction: "asc" | "desc" }) {
  return (
    <svg
      width="10"
      height="10"
      viewBox="0 0 10 10"
      fill="currentColor"
      style={{ marginLeft: "4px", opacity: 0.8 }}
    >
      {direction === "asc" ? (
        <polygon points="5,2 9,8 1,8" />
      ) : (
        <polygon points="5,8 1,2 9,2" />
      )}
    </svg>
  );
}

function CopyIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
      <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
    </svg>
  );
}

function CheckIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="var(--success, #52c41a)"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="20 6 9 17 4 12" />
    </svg>
  );
}

function EventLevelIcon({ level }: { level: LogLevel }) {
  switch (level) {
    case "Info":
      return (
        <span
          className="badge badge-primary"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          INFO
        </span>
      );
    case "Warn":
      return (
        <span
          className="badge badge-queued"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          WARN
        </span>
      );
    case "Error":
      return (
        <span
          className="badge badge-error"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          ERROR
        </span>
      );
    case "Debug":
      return (
        <span
          className="badge badge-secondary"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          DEBUG
        </span>
      );
    case "Trace":
      return (
        <span
          className="badge badge-secondary"
          style={{ fontSize: "0.75rem", padding: "0.15rem 0.45rem" }}
        >
          TRACE
        </span>
      );
  }
}

interface EventDetailsModalProps {
  event: EventEntry;
  onClose: () => void;
}

function EventDetailsModal({ event, onClose }: EventDetailsModalProps) {
  const [stackTraceExpanded, setStackTraceExpanded] = useState(true);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        onClose();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const handleCopy = useCallback(async () => {
    const textToCopy = [
      `Timestamp: ${event.timestamp} (${formatEventTime(event.timestamp)})`,
      `Level: ${event.level}`,
      `Component: ${event.component}`,
      `Message: ${event.message}`,
      event.exception ? `Exception:\n${event.exception}` : null,
    ]
      .filter(Boolean)
      .join("\n");

    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(textToCopy);
      } else {
        const textarea = document.createElement("textarea");
        textarea.value = textToCopy;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand("copy");
        document.body.removeChild(textarea);
      }
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error("Failed to copy event to clipboard", err);
    }
  }, [event]);

  return (
    <div
      className="modal-overlay"
      onClick={handleBackdropClick}
      role="dialog"
      aria-modal="true"
      aria-labelledby="event-details-title"
    >
      <div
        className="modal"
        style={{
          maxWidth: "760px",
          width: "92%",
          maxHeight: "85vh",
          display: "flex",
          flexDirection: "column",
          padding: "1.5rem",
          boxShadow:
            "0 12px 40px rgba(0, 0, 0, 0.6), 0 2px 8px rgba(0, 0, 0, 0.3)",
          border: "1px solid var(--border-light)",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1.25rem",
            paddingBottom: "0.75rem",
            borderBottom: "1px solid var(--border-light)",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}>
            <EventLevelIcon level={event.level} />
            <h2
              id="event-details-title"
              className="modal-title"
              style={{ margin: 0, fontSize: "1.2rem", fontWeight: 600 }}
            >
              Event Details
            </h2>
          </div>
          <button
            type="button"
            className="btn btn-outline"
            style={{
              padding: "0.2rem 0.5rem",
              fontSize: "0.85rem",
              borderRadius: "4px",
            }}
            onClick={onClose}
            title="Close dialog"
            aria-label="Close event details"
          >
            ✕
          </button>
        </div>

        {/* Body */}
        <div style={{ overflowY: "auto", flex: 1, paddingRight: "0.25rem" }}>
          {/* Metadata Grid */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "auto 1fr",
              columnGap: "1.25rem",
              rowGap: "0.6rem",
              marginBottom: "1.25rem",
              fontSize: "0.85rem",
            }}
          >
            <span style={{ color: "var(--text-muted)", fontWeight: 600 }}>
              Timestamp:
            </span>
            <div>
              <span style={{ fontWeight: 500 }}>
                {formatEventTime(event.timestamp)}
              </span>
              <span
                style={{
                  marginLeft: "0.5rem",
                  color: "var(--text-dim)",
                  fontSize: "0.78rem",
                }}
              >
                ({event.timestamp})
              </span>
            </div>

            <span style={{ color: "var(--text-muted)", fontWeight: 600 }}>
              Component:
            </span>
            <div>
              <code
                style={{
                  fontSize: "0.82rem",
                  color: "var(--accent, #c8a84e)",
                  backgroundColor: "var(--sidebar-bg)",
                  padding: "0.15rem 0.4rem",
                  borderRadius: "3px",
                }}
              >
                {event.component}
              </code>
            </div>

            <span style={{ color: "var(--text-muted)", fontWeight: 600 }}>
              Level:
            </span>
            <div>
              <span style={{ fontWeight: 600 }}>{event.level}</span>
            </div>
          </div>

          {/* Message section */}
          <div style={{ marginBottom: "1.25rem" }}>
            <div
              style={{
                fontSize: "0.85rem",
                fontWeight: 600,
                color: "var(--text-muted)",
                marginBottom: "0.4rem",
              }}
            >
              Message
            </div>
            <div
              style={{
                backgroundColor: "var(--sidebar-bg, #14151b)",
                border: "1px solid var(--border-light)",
                borderRadius: "6px",
                padding: "0.85rem",
                fontFamily: "var(--font-mono, monospace)",
                fontSize: "0.82rem",
                lineHeight: 1.5,
                whiteSpace: "pre-wrap",
                wordBreak: "break-word",
                maxHeight: "180px",
                overflowY: "auto",
              }}
            >
              {event.message}
            </div>
          </div>

          {/* Exception Stack Trace section */}
          {event.exception && (
            <div style={{ marginBottom: "1rem" }}>
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginBottom: "0.4rem",
                }}
              >
                <button
                  type="button"
                  className="btn btn-small btn-outline"
                  onClick={() => setStackTraceExpanded((prev) => !prev)}
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.4rem",
                    fontSize: "0.82rem",
                  }}
                >
                  <span>{stackTraceExpanded ? "▼" : "▶"}</span>
                  <span>
                    {stackTraceExpanded
                      ? "Hide Exception Stack Trace"
                      : "Show Exception Stack Trace"}
                  </span>
                </button>
              </div>

              {stackTraceExpanded && (
                <pre
                  style={{
                    margin: 0,
                    backgroundColor: "var(--sidebar-bg, #14151b)",
                    border: "1px solid var(--border-light)",
                    borderRadius: "6px",
                    padding: "0.85rem",
                    fontFamily: "var(--font-mono, monospace)",
                    fontSize: "0.78rem",
                    lineHeight: 1.5,
                    color: "var(--danger, #e55353)",
                    whiteSpace: "pre-wrap",
                    wordBreak: "break-word",
                    maxHeight: "260px",
                    overflowY: "auto",
                  }}
                >
                  {event.exception}
                </pre>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginTop: "1.25rem",
            paddingTop: "0.75rem",
            borderTop: "1px solid var(--border-light)",
            flexWrap: "wrap",
            gap: "0.5rem",
          }}
        >
          <button
            type="button"
            className="btn btn-small btn-outline"
            onClick={handleCopy}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            {copied ? (
              <>
                <CheckIcon />
                <span style={{ color: "var(--success, #52c41a)" }}>
                  Copied to Clipboard!
                </span>
              </>
            ) : (
              <>
                <CopyIcon />
                <span>Copy to Clipboard</span>
              </>
            )}
          </button>

          <button
            type="button"
            className="btn btn-small btn-primary"
            onClick={onClose}
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

function SystemEvents() {
  const { data: entries, isLoading, isError } = useEventEntries();
  const queryClient = useQueryClient();
  const [sortDirection, setSortDirection] = useState<"asc" | "desc">("desc");
  const [levelFilter, setLevelFilter] = useState<LevelFilter>("All");
  const [searchText, setSearchText] = useState("");
  const [pageSize, setPageSize] = useState<number>(50);
  const [currentPage, setCurrentPage] = useState<number>(1);
  const [clearedAt, setClearedAt] = useState<number | null>(null);
  const [selectedEvent, setSelectedEvent] = useState<EventEntry | null>(null);
  const tableBodyRef = useRef<HTMLDivElement>(null);

  const filteredEntries = useMemo(() => {
    if (!entries) return [];

    let result = entries;

    if (clearedAt !== null) {
      result = result.filter(
        (entry) => new Date(entry.timestamp).getTime() > clearedAt,
      );
    }

    if (levelFilter !== "All") {
      result = result.filter(
        (entry) => entry.level.toLowerCase() === levelFilter.toLowerCase(),
      );
    }

    if (searchText.trim()) {
      const q = searchText.trim().toLowerCase();
      result = result.filter(
        (entry) =>
          entry.component.toLowerCase().includes(q) ||
          entry.message.toLowerCase().includes(q) ||
          (entry.exception && entry.exception.toLowerCase().includes(q)),
      );
    }

    const sorted = [...result];
    sorted.sort((a, b) => {
      const ta = new Date(a.timestamp).getTime();
      const tb = new Date(b.timestamp).getTime();
      return sortDirection === "desc" ? tb - ta : ta - tb;
    });

    return sorted;
  }, [entries, sortDirection, clearedAt, levelFilter, searchText]);

  const totalPages = Math.max(1, Math.ceil(filteredEntries.length / pageSize));
  const activePage = Math.min(Math.max(1, currentPage), totalPages);

  const startIndex = (activePage - 1) * pageSize;
  const endIndex = Math.min(startIndex + pageSize, filteredEntries.length);
  const currentEntries = useMemo(() => {
    return filteredEntries.slice(startIndex, endIndex);
  }, [filteredEntries, startIndex, endIndex]);

  const pageNumbers = useMemo(() => {
    return getPageNumbers(activePage, totalPages);
  }, [activePage, totalPages]);

  const handleRefresh = useCallback(() => {
    setClearedAt(null);
    queryClient.invalidateQueries({ queryKey: ["system", "events"] });
  }, [queryClient]);

  const handleClear = useCallback(() => {
    setClearedAt(Date.now());
    setCurrentPage(1);
  }, []);

  const toggleSort = useCallback(() => {
    setSortDirection((prev) => (prev === "desc" ? "asc" : "desc"));
  }, []);

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
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
            <span>📜</span> System: Events
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Real-time audit log events, application exceptions, and background
            routine updates
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          <button
            className="btn btn-outline btn-small"
            onClick={handleRefresh}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            <RefreshIcon />
            <span>Refresh</span>
          </button>
          <button
            className="btn btn-outline btn-small"
            onClick={handleClear}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.4rem",
            }}
          >
            <ClearIcon />
            <span>Clear</span>
          </button>
        </div>
      </div>

      {/* Events Table Card */}
      <div
        className="card"
        style={{
          borderRadius: "8px",
          border: "1px solid var(--border-light)",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          padding: 0,
          overflow: "hidden",
        }}
      >
        {/* Filter and Search Toolbar */}
        <div className="log-toolbar">
          <div className="log-toolbar-filters">
            {LEVEL_FILTER_OPTIONS.map((level) => (
              <button
                key={level}
                type="button"
                title={
                  level === "All"
                    ? "Show all events"
                    : `Show ${level} events`
                }
                className={`btn btn-small ${levelFilter === level ? "log-filter-active" : ""} ${level !== "All" ? `log-filter-${level.toLowerCase()}` : ""}`}
                onClick={() => {
                  setLevelFilter(level);
                  setCurrentPage(1);
                }}
              >
                {level}
              </button>
            ))}
          </div>

          <div className="log-toolbar-actions">
            <input
              type="text"
              className="search-input"
              placeholder="Filter component or message..."
              value={searchText}
              onChange={(e) => {
                setSearchText(e.target.value);
                setCurrentPage(1);
              }}
              style={{ width: "220px" }}
            />
            {searchText && (
              <button
                type="button"
                className="btn btn-small btn-outline"
                onClick={() => {
                  setSearchText("");
                  setCurrentPage(1);
                }}
                title="Clear search"
              >
                ✕
              </button>
            )}
          </div>
        </div>

        {/* Table View */}
        <div className="torrent-table-wrapper" ref={tableBodyRef}>
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th" style={{ width: "90px" }}>
                  Level
                </th>
                <th
                  className="torrent-table-th"
                  onClick={toggleSort}
                  style={{
                    width: "190px",
                    cursor: "pointer",
                    userSelect: "none",
                  }}
                  title="Click to sort by timestamp"
                >
                  <span
                    style={{ display: "inline-flex", alignItems: "center" }}
                  >
                    Time <SortArrow direction={sortDirection} />
                  </span>
                </th>
                <th className="torrent-table-th" style={{ width: "220px" }}>
                  Component / Logger
                </th>
                <th className="torrent-table-th">Event Message</th>
              </tr>
            </thead>
            <tbody>
              {isLoading && (
                <tr>
                  <td colSpan={4} className="torrent-table-empty">
                    Loading event stream...
                  </td>
                </tr>
              )}
              {!isLoading && isError && (
                <tr>
                  <td colSpan={4} className="torrent-table-empty">
                    Failed to load events.
                  </td>
                </tr>
              )}
              {!isLoading && !isError && filteredEntries.length === 0 && (
                <tr>
                  <td colSpan={4} className="torrent-table-empty">
                    {searchText || levelFilter !== "All"
                      ? "No events match the selected filters."
                      : "No recent events logged."}
                  </td>
                </tr>
              )}
              {currentEntries.map((entry) => (
                <tr
                  key={entry.id}
                  className="torrent-table-row"
                  onClick={() => setSelectedEvent(entry)}
                  style={{ cursor: "pointer" }}
                  title="Click to view event details"
                >
                  <td>
                    <EventLevelIcon level={entry.level} />
                  </td>
                  <td
                    style={{
                      color: "var(--text-muted)",
                      fontSize: "0.85rem",
                      whiteSpace: "nowrap",
                    }}
                    title={entry.timestamp}
                  >
                    {formatEventTime(entry.timestamp)}
                  </td>
                  <td>
                    <code
                      style={{
                        fontSize: "0.8rem",
                        color: "var(--accent, #c8a84e)",
                      }}
                    >
                      {entry.component}
                    </code>
                  </td>
                  <td style={{ fontSize: "0.85rem" }}>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "baseline",
                        justifyContent: "space-between",
                        gap: "0.75rem",
                      }}
                    >
                      <span
                        style={{
                          whiteSpace: "pre-wrap",
                          wordBreak: "break-word",
                          display: "-webkit-box",
                          WebkitLineClamp: 3,
                          WebkitBoxOrient: "vertical",
                          overflow: "hidden",
                        }}
                      >
                        {entry.message}
                      </span>
                      {entry.exception && (
                        <span
                          className="badge badge-error"
                          style={{
                            fontSize: "0.7rem",
                            padding: "0.1rem 0.35rem",
                            flexShrink: 0,
                          }}
                          title="Has exception stack trace"
                        >
                          Exception
                        </span>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Pagination Card Footer */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            padding: "0.75rem 1rem",
            backgroundColor: "var(--bg-secondary)",
            borderTop: "1px solid var(--border-light)",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          {/* Left: Range info & Page Size selector */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "1.25rem",
              flexWrap: "wrap",
            }}
          >
            <span style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
              {filteredEntries.length === 0
                ? "Showing 0-0 of 0"
                : `Showing ${startIndex + 1}-${endIndex} of ${filteredEntries.length}`}
            </span>

            <div
              style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
            >
              <span style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
                Per page:
              </span>
              <select
                className="form-select"
                value={pageSize}
                onChange={(e) => {
                  setPageSize(Number(e.target.value));
                  setCurrentPage(1);
                }}
                style={{
                  padding: "0.25rem 1.75rem 0.25rem 0.5rem",
                  fontSize: "0.82rem",
                  width: "auto",
                  minWidth: "70px",
                  height: "30px",
                }}
              >
                {PAGE_SIZE_OPTIONS.map((size) => (
                  <option key={size} value={size}>
                    {size}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Right: Page Navigation */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.35rem",
              flexWrap: "wrap",
            }}
          >
            <button
              type="button"
              className="btn btn-small btn-outline"
              onClick={() => setCurrentPage(1)}
              disabled={activePage <= 1}
              title="First Page"
              style={{ minWidth: "30px", padding: "0.25rem 0.5rem" }}
            >
              «
            </button>
            <button
              type="button"
              className="btn btn-small btn-outline"
              onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
              disabled={activePage <= 1}
              title="Previous Page"
              style={{ minWidth: "30px", padding: "0.25rem 0.5rem" }}
            >
              ‹
            </button>

            {pageNumbers.map((p, idx) =>
              typeof p === "number" ? (
                <button
                  key={p}
                  type="button"
                  className={`btn btn-small ${activePage === p ? "log-filter-active" : "btn-outline"}`}
                  onClick={() => setCurrentPage(p)}
                  style={{ minWidth: "30px", padding: "0.25rem 0.5rem" }}
                >
                  {p}
                </button>
              ) : (
                <span
                  key={`ellipsis-${idx}`}
                  style={{
                    padding: "0 0.35rem",
                    color: "var(--text-muted)",
                    userSelect: "none",
                  }}
                >
                  {p}
                </span>
              ),
            )}

            <button
              type="button"
              className="btn btn-small btn-outline"
              onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))}
              disabled={activePage >= totalPages}
              title="Next Page"
              style={{ minWidth: "30px", padding: "0.25rem 0.5rem" }}
            >
              ›
            </button>
            <button
              type="button"
              className="btn btn-small btn-outline"
              onClick={() => setCurrentPage(totalPages)}
              disabled={activePage >= totalPages}
              title="Last Page"
              style={{ minWidth: "30px", padding: "0.25rem 0.5rem" }}
            >
              »
            </button>
          </div>
        </div>
      </div>

      {/* Event Details Modal */}
      {selectedEvent && (
        <EventDetailsModal
          event={selectedEvent}
          onClose={() => setSelectedEvent(null)}
        />
      )}
    </div>
  );
}

export default SystemEvents;
