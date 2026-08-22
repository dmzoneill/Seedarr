import { useEffect, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useSignalR } from "../api/signalr";
import { useToast } from "../context/ToastContext";

const EVENT_INVALIDATION_MAP: Record<string, string[][]> = {
  TorrentAdded: [["torrents"]],
  TorrentUpdated: [["torrents"]],
  TorrentDeleted: [["torrents"]],
  SeedingStatsUpdated: [["seeding", "stats"]],
  HealthCheckCompleted: [["health"]],
  CommandCompleted: [["system", "status"]],
};

export default function SignalRProvider() {
  const queryClient = useQueryClient();
  const { connection, status } = useSignalR(queryClient);
  const { showToast } = useToast();
  const showToastRef = useRef(showToast);
  showToastRef.current = showToast;

  useEffect(() => {
    // 1. Generic receiveMessage dispatcher from Seedarr REST controller SignalR broadcasts
    const onReceiveMessage = (msg: unknown) => {
      if (!msg || typeof msg !== "object") return;
      const message = msg as {
        name?: string;
        body?: unknown;
        action?: string;
      };
      const name = (message.name ?? "").toLowerCase();

      if (name === "torrent" || name.includes("torrent")) {
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
        const bodyObj = message.body as Record<string, unknown> | undefined;
        if (bodyObj?.id && typeof bodyObj.id === "number") {
          queryClient.invalidateQueries({
            queryKey: ["torrents", bodyObj.id],
          });
          queryClient.invalidateQueries({
            queryKey: ["torrents", bodyObj.id, "trackers"],
          });
        }
      } else if (name.includes("tracker")) {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
      } else if (name.includes("seeding")) {
        queryClient.invalidateQueries({ queryKey: ["seeding", "stats"] });
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
      } else if (name.includes("health")) {
        queryClient.invalidateQueries({ queryKey: ["health"] });
      } else if (name.includes("command") || name.includes("system")) {
        queryClient.invalidateQueries({ queryKey: ["system", "status"] });
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
        }
      };
      handlers.push([event, handler]);
      connection.on(event, handler);
    }

    return () => {
      connection.off("receiveMessage", onReceiveMessage);
      for (const [event, handler] of handlers) {
        connection.off(event, handler);
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
