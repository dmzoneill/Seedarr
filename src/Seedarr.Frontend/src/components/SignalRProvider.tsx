import { useEffect, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useSignalR } from "../api/signalr";
import { useToast } from "../context/ToastContext";

export const EVENT_INVALIDATION_MAP: Record<string, string[][]> = {
  TorrentAdded: [["torrents"], ["trackerboost"]],
  TorrentUpdated: [["torrents"], ["trackerboost"]],
  TorrentDeleted: [["torrents"], ["trackerboost"]],
  SeedingStatsUpdated: [["seeding", "stats"]],
  HealthCheckCompleted: [["health"]],
  CommandStarted: [["system", "status"], ["system", "commands"]],
  CommandCompleted: [["system", "status"], ["system", "commands"]],
  TaskStarted: [["system", "tasks"], ["system", "status"]],
  TaskCompleted: [["system", "tasks"], ["system", "status"]],
  AutomationExecuted: [["automation", "scripts"], ["automation"]],
  AutomationTriggerEvaluated: [["automation", "scripts"], ["automation"]],
  TrackerUpdated: [["trackerboost"]],
  TrackerAnnounced: [["trackerboost"]],
  trackerUpdated: [["trackerboost"]],
  trackerAnnounced: [["trackerboost"]],
  TrackerAnnounceEvent: [["trackerboost"]],
};

export const RECONNECT_QUERY_KEYS: string[][] = [
  ["torrents"],
  ["trackerboost"],
  ["seeding", "stats"],
  ["seeding", "history"],
  ["categories"],
  ["tags"],
  ["health"],
  ["system", "status"],
  ["system", "commands"],
  ["system", "tasks"],
  ["diskspace"],
  ["automation"],
  ["automation", "scripts"],
];

/**
 * Determines whether a SignalR message name is handled by dedicated named event handlers
 * (e.g. TorrentAdded, SeedingStatsUpdated, HealthCheckCompleted, CommandStarted, etc.)
 * to avoid duplicate query invalidation storms from generic receiveMessage dispatch.
 */
export function isHandledByNamedEvent(name?: string): boolean {
  if (!name) return false;
  const lower = name.toLowerCase();
  return (
    lower.includes("torrent") ||
    lower.includes("seeding") ||
    lower.includes("health") ||
    lower.includes("command") ||
    lower.includes("system") ||
    lower.includes("task") ||
    lower.includes("automation") ||
    lower === "trackerupdated" ||
    lower === "trackerannounced" ||
    lower === "trackerannounceevent"
  );
}

export default function SignalRProvider() {
  const queryClient = useQueryClient();
  const { connection, status } = useSignalR(queryClient);
  const { showToast } = useToast();
  const showToastRef = useRef(showToast);
  showToastRef.current = showToast;

  useEffect(() => {
    // 1. Generic receiveMessage dispatcher for unmapped events only (e.g. tracker, category, tag)
    const onReceiveMessage = (msg: unknown) => {
      if (!msg || typeof msg !== "object") return;
      const message = msg as {
        name?: string;
        body?: unknown;
        action?: string;
      };
      const name = (message.name ?? "").toLowerCase();

      // Eliminate duplicate invalidations for events already handled by named event listeners
      if (isHandledByNamedEvent(name)) {
        return;
      }

      if (name.includes("tracker")) {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
        const bodyObj = message.body as Record<string, unknown> | undefined;
        const torrentId =
          typeof bodyObj?.torrentId === "number"
            ? bodyObj.torrentId
            : typeof bodyObj?.TorrentId === "number"
            ? bodyObj.TorrentId
            : undefined;
        if (torrentId) {
          queryClient.invalidateQueries({
            queryKey: ["torrents", torrentId, "trackers"],
          });
        }
      } else if (name.includes("category")) {
        queryClient.invalidateQueries({ queryKey: ["categories"] });
      } else if (name.includes("tag")) {
        queryClient.invalidateQueries({ queryKey: ["tags"] });
      }
    };

    connection.on("receiveMessage", onReceiveMessage);

    // 2. Direct named event handlers
    const handlers: Array<[string, (data?: unknown) => void]> = [];

    for (const [event, queryKeys] of Object.entries(EVENT_INVALIDATION_MAP)) {
      const handler = (data?: unknown) => {
        for (const key of queryKeys) {
          queryClient.invalidateQueries({ queryKey: key });
        }

        // Entity-specific invalidations for torrents
        if (
          event === "TorrentAdded" ||
          event === "TorrentUpdated" ||
          event === "TorrentDeleted"
        ) {
          const bodyObj = data as Record<string, unknown> | undefined;
          if (bodyObj?.id && typeof bodyObj.id === "number") {
            queryClient.invalidateQueries({
              queryKey: ["torrents", bodyObj.id],
            });
            queryClient.invalidateQueries({
              queryKey: ["torrents", bodyObj.id, "trackers"],
            });
          }
        }

        // Entity-specific invalidations for trackers
        if (
          event === "TrackerUpdated" ||
          event === "TrackerAnnounced" ||
          event === "trackerUpdated" ||
          event === "trackerAnnounced" ||
          event === "TrackerAnnounceEvent"
        ) {
          const bodyObj = data as Record<string, unknown> | undefined;
          const torrentId =
            typeof bodyObj?.torrentId === "number"
              ? bodyObj.torrentId
              : typeof bodyObj?.TorrentId === "number"
              ? bodyObj.TorrentId
              : undefined;

          if (torrentId) {
            queryClient.invalidateQueries({
              queryKey: ["torrents", torrentId, "trackers"],
            });
          }
        }

        // Fire toast notifications for key events
        if (event === "TorrentAdded") {
          const name =
            data && typeof data === "object" && "name" in data
              ? String((data as Record<string, unknown>).name)
              : undefined;
          showToastRef.current(
            name ? `Torrent added: ${name}` : "Torrent added",
            "success",
          );
        } else if (event === "TorrentDeleted") {
          showToastRef.current("Torrent removed", "info");
        } else if (event === "AutomationExecuted") {
          const body = data as Record<string, unknown> | undefined;
          const isSuccess = body?.success !== false && body?.Success !== false;
          showToastRef.current(
            isSuccess
              ? "Automation execution completed"
              : "Automation execution encountered error",
            isSuccess ? "success" : "error",
          );
        }
      };
      handlers.push([event, handler]);
      connection.on(event, handler);
    }

    // 3. Reconnection query cache synchronization
    const handleReconnected = () => {
      for (const key of RECONNECT_QUERY_KEYS) {
        queryClient.invalidateQueries({ queryKey: key });
      }
    };

    connection.onreconnected(handleReconnected);

    return () => {
      connection.off("receiveMessage", onReceiveMessage);
      for (const [event, handler] of handlers) {
        connection.off(event, handler);
      }
      const callbacks = (
        connection as unknown as { _reconnectedCallbacks?: Array<unknown> }
      )._reconnectedCallbacks;
      if (callbacks) {
        const idx = callbacks.indexOf(handleReconnected);
        if (idx !== -1) {
          callbacks.splice(idx, 1);
        }
      }
    };
  }, [connection, queryClient]);

  const dotColor =
    status === "connected"
      ? "var(--signalr-connected, #22c55e)"
      : status === "reconnecting"
        ? "var(--signalr-reconnecting, #f59e0b)"
        : "var(--signalr-disconnected, #ef4444)";

  const title =
    status === "connected"
      ? "Real-time: connected"
      : status === "reconnecting"
        ? "Real-time: reconnecting..."
        : "Real-time: disconnected";

  return (
    <span
      title={title}
      aria-label={title}
      style={{
        display: "inline-block",
        width: 8,
        height: 8,
        borderRadius: "50%",
        backgroundColor: dotColor,
        position: "fixed",
        bottom: 12,
        right: 12,
        zIndex: 9999,
        transition: "background-color 0.3s ease",
      }}
    />
  );
}
