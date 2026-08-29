import { useState, useEffect, useRef, useMemo, useCallback } from "react";
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
}

const ALL_LEVELS: LogLevel[] = ["Trace", "Debug", "Info", "Warn", "Error"];

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
      const data = await apiClient.get<ApiLogEntry[]>("/log");
      return data.map((entry) => ({
        id: entry.id,
        timestamp: entry.time,
        level: toLogLevel(entry.level),
        component: entry.logger,
        message: entry.exception
          ? `${entry.message}\n${entry.exception}`
          : entry.message,
      }));
    },
    refetchInterval: 10000,
  });
}

function formatEventTime(iso: string): string {
  const d = new Date(iso);
  let hours = d.getHours();
  const minutes = d.getMinutes().toString().padStart(2, "0");
  const ampm = hours >= 12 ? "pm" : "am";
  hours = hours % 12;
  if (hours === 0) hours = 12;
  return `${hours}:${minutes}${ampm}`;
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
      style={{ marginLeft: "4px", opacity: 0.6 }}
    >
      {direction === "asc" ? (
        <polygon points="5,2 9,8 1,8" />
      ) : (
        <polygon points="5,8 1,2 9,2" />
      )}
    </svg>
  );
}

function EventLevelIcon({ level }: { level: LogLevel }) {
  switch (level) {
    case "Info":
      return (
        <span className="event-level-icon event-level-info" title="Info">
          <svg width="16" height="16" viewBox="0 0 16 16">
            <circle cx="8" cy="8" r="7" fill="currentColor" opacity="0.2" />
            <circle
              cx="8"
              cy="8"
              r="7"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.5"
            />
            <text
              x="8"
              y="12"
              textAnchor="middle"
              fill="currentColor"
              fontSize="10"
              fontWeight="700"
              fontFamily="sans-serif"
            >
              i
            </text>
          </svg>
        </span>
      );
    case "Warn":
      return (
        <span className="event-level-icon event-level-warn" title="Warning">
          <svg width="16" height="16" viewBox="0 0 16 16">
            <polygon
              points="8,1 15,15 1,15"
              fill="currentColor"
              opacity="0.2"
            />
            <polygon
              points="8,1 15,15 1,15"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.2"
              strokeLinejoin="round"
            />
            <text
              x="8"
              y="13"
              textAnchor="middle"
              fill="currentColor"
              fontSize="10"
              fontWeight="700"
              fontFamily="sans-serif"
            >
              !
            </text>
          </svg>
        </span>
      );
    case "Error":
      return (
        <span className="event-level-icon event-level-error" title="Error">
          <svg width="16" height="16" viewBox="0 0 16 16">
            <circle cx="8" cy="8" r="7" fill="currentColor" opacity="0.2" />
            <circle
              cx="8"
              cy="8"
              r="7"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.5"
            />
            <line
              x1="5"
              y1="5"
              x2="11"
              y2="11"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
            />
            <line
              x1="11"
              y1="5"
              x2="5"
              y2="11"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
            />
          </svg>
        </span>
      );
    case "Debug":
      return (
        <span className="event-level-icon event-level-debug" title="Debug">
          <svg width="16" height="16" viewBox="0 0 16 16">
            <circle cx="8" cy="8" r="7" fill="currentColor" opacity="0.2" />
            <circle
              cx="8"
              cy="8"
              r="7"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.5"
            />
          </svg>
        </span>
      );
    case "Trace":
      return (
        <span className="event-level-icon event-level-debug" title="Trace">
          <svg width="16" height="16" viewBox="0 0 16 16">
            <circle cx="8" cy="8" r="7" fill="currentColor" opacity="0.15" />
            <circle
              cx="8"
              cy="8"
              r="7"
              fill="none"
              stroke="currentColor"
              strokeWidth="1"
            />
          </svg>
        </span>
      );
  }
}

function SystemEvents() {
  const { data: entries, isLoading, isError } = useEventEntries();
  const queryClient = useQueryClient();
  const [sortDirection, setSortDirection] = useState<"asc" | "desc">("desc");
  const [autoScroll, setAutoScroll] = useState(true);
  const [cleared, setCleared] = useState(false);
  const tableBodyRef = useRef<HTMLDivElement>(null);

  const displayEntries = useMemo(() => {
    if (cleared) return [];
    if (!entries) return [];
    const sorted = [...entries];
    sorted.sort((a, b) => {
      const ta = new Date(a.timestamp).getTime();
      const tb = new Date(b.timestamp).getTime();
      return sortDirection === "desc" ? tb - ta : ta - tb;
    });
    return sorted;
  }, [entries, sortDirection, cleared]);

  const handleRefresh = useCallback(() => {
    setCleared(false);
    queryClient.invalidateQueries({ queryKey: ["system", "events"] });
  }, [queryClient]);

  const handleClear = useCallback(() => {
    setCleared(true);
  }, []);

  const toggleSort = useCallback(() => {
    setSortDirection((prev) => (prev === "desc" ? "asc" : "desc"));
  }, []);

  // Auto-scroll to bottom when new entries arrive
  useEffect(() => {
    if (autoScroll && tableBodyRef.current) {
      tableBodyRef.current.scrollTop = tableBodyRef.current.scrollHeight;
    }
  }, [displayEntries, autoScroll]);

  return (
    <div className="events-page">
      <h1 className="page-heading">Events</h1>

      <div className="events-toolbar">
        <div className="events-toolbar-actions">
          <button className="btn btn-toolbar" onClick={handleRefresh}>
            <RefreshIcon />
            <span>Refresh</span>
          </button>
          <button className="btn btn-toolbar" onClick={handleClear}>
            <ClearIcon />
            <span>Clear</span>
          </button>
        </div>
      </div>

      <div className="events-table-wrapper" ref={tableBodyRef}>
        <table className="torrent-table">
          <thead>
            <tr>
              <th className="torrent-table-th" style={{ width: "28px" }}></th>
              <th
                className="torrent-table-th events-time-col"
                onClick={toggleSort}
                style={{ width: "100px" }}
              >
                Time
                <SortArrow direction={sortDirection} />
              </th>
              <th className="torrent-table-th" style={{ width: "180px" }}>
                Component
              </th>
              <th className="torrent-table-th">Message</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={4} className="torrent-table-empty">
                  Loading events...
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
            {!isLoading && !isError && displayEntries.length === 0 && (
              <tr>
                <td colSpan={4} className="torrent-table-empty">
                  No events
                </td>
              </tr>
            )}
            {displayEntries.map((entry) => (
              <tr key={entry.id} className="torrent-table-row">
                <td style={{ textAlign: "center", padding: "0.5rem 0.5rem" }}>
                  <EventLevelIcon level={entry.level} />
                </td>
                <td>{formatEventTime(entry.timestamp)}</td>
                <td className="events-component">{entry.component}</td>
                <td className="events-message">{entry.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default SystemEvents;
