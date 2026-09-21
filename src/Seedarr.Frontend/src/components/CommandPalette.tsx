import { useState, useEffect, useRef, useMemo, useDeferredValue } from "react";
import type { DownloadHistoryEntry } from "../api/types";
import { filterAndRankItems } from "../utils/fuzzySearch";
import { useNavigate } from "react-router";
import { useModalRegistration } from "./ModalProvider";
import {
  useTorrents,
  useDownloadHistory,
  useBoostAllTorrents,
  useHarvestDownloadTrackers,
  useHarvestProwlarrTrackers,
  useScanTrackerBoostTrackers,
  useGeneralConfig,
  useStartAllSeeding,
  useStopAllSeeding,
  useSeedingConfig,
  useSaveSeedingConfig,
} from "../api/hooks";
import { useTheme } from "../context/ThemeContext";
import { useToast } from "../context/ToastContext";
import { apiClient } from "../api/client";
import { formatBytes } from "../utils/formatters";
import MediaArtwork from "./MediaArtwork";
import { trackModalOpen } from "../utils/analytics";

interface CommandItem {
  id: string;
  category: "Navigation" | "Torrents" | "Actions" | "Settings";
  title: string;
  subtitle?: string;
  icon: string;
  posterUrl?: string | null;
  badge?: string;
  badgeClass?: string;
  onSelect: () => void;
}

interface CommandPaletteProps {
  isOpen: boolean;
  onClose: () => void;
  onOpenShortcuts?: () => void;
  onOpenAddTorrent?: () => void;
  onOpenGettingStarted?: () => void;
}

export function CommandPalette({
  isOpen,
  onClose,
  onOpenShortcuts,
  onOpenAddTorrent,
  onOpenGettingStarted,
}: CommandPaletteProps) {
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const modalRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  useModalRegistration({
    id: "command-palette",
    isOpen,
    onClose,
    modalRef,
  });

  useEffect(() => {
    if (isOpen) {
      trackModalOpen("command_palette");
    }
  }, [isOpen]);

  const { data: torrents } = useTorrents();
  const { data: history } = useDownloadHistory();
  const { data: generalConfig } = useGeneralConfig();
  const { theme, toggleTheme } = useTheme();
  const { showToast } = useToast();

  const boostAll = useBoostAllTorrents();
  const harvestSwarm = useHarvestDownloadTrackers();
  const syncProwlarr = useHarvestProwlarrTrackers();
  const scanTrackers = useScanTrackerBoostTrackers();
  const startAll = useStartAllSeeding();
  const stopAll = useStopAllSeeding();
  const { data: seedingConfig } = useSeedingConfig();
  const saveSeedingConfig = useSaveSeedingConfig();

  const isKeyboardNavigating = useRef(false);
  const lastPointerPos = useRef({ x: -1, y: -1 });

  useEffect(() => {
    if (isOpen) {
      setQuery("");
      setSelectedIndex(0);
      isKeyboardNavigating.current = false;
      lastPointerPos.current = { x: -1, y: -1 };
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;

    const handleGlobalKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        onClose();
      }
    };

    window.addEventListener("keydown", handleGlobalKeyDown, true);
    return () =>
      window.removeEventListener("keydown", handleGlobalKeyDown, true);
  }, [isOpen, onClose]);

  const items = useMemo<CommandItem[]>(() => {
    if (!isOpen) return [];
    const list: CommandItem[] = [];

    // 1. Navigation items
    const navs: {
      path: string;
      title: string;
      subtitle: string;
      icon: string;
    }[] = [
      {
        path: "/",
        title: "Dashboard",
        subtitle: "Overview, stats, charts and recent activity",
        icon: "📊",
      },
      {
        path: "/torrents",
        title: "Torrents Index",
        subtitle: "Active torrents, seed list, filtering and management",
        icon: "📦",
      },
      {
        path: "/activity/history",
        title: "Download History",
        subtitle: "Enriched media library history and captured downloads",
        icon: "📜",
      },
      {
        path: "/activity/metrics",
        title: "Activity Metrics",
        subtitle: "Real-time bandwidth, peer connections and transfer charts",
        icon: "📈",
      },
      {
        path: "/tracker/trackerboost",
        title: "Tracker Boost",
        subtitle: "Swarm optimizer, BEP 15/48 live scraping and cross-matrix",
        icon: "⚡",
      },
      {
        path: "/tracker/inbuilt",
        title: "Inbuilt Tracker Server",
        subtitle: "Local BitTorrent tracker daemon and announced swarms",
        icon: "📡",
      },
      {
        path: "/tracker/metrics",
        title: "Tracker Metrics",
        subtitle: "Telemetry, traffic statistics, scrape responses and latency",
        icon: "🌐",
      },
      {
        path: "/peermap",
        title: "Peer Map",
        subtitle: "Global swarm distribution and GeoIP connections",
        icon: "🗺️",
      },
      {
        path: "/schedule",
        title: "Speed Schedule",
        subtitle: "Time-based bandwidth rules and alternate speed limits",
        icon: "🕒",
      },
      {
        path: "/statistics",
        title: "Statistics & Achievements",
        subtitle: "Seeding milestones, ratio records and buffer stats",
        icon: "🏆",
      },
    ];

    navs.forEach((n) => {
      list.push({
        id: `nav-${n.path}`,
        category: "Navigation",
        title: n.title,
        subtitle: n.subtitle,
        icon: n.icon,
        onSelect: () => {
          navigate(n.path);
          onClose();
        },
      });
    });

    // 2. Settings subpages
    const settingsSub: { path: string; title: string; subtitle: string }[] = [
      {
        path: "/settings/general",
        title: "General Settings",
        subtitle: "Application port, API key and watch folder",
      },
      {
        path: "/settings/bittorrent",
        title: "BitTorrent Settings",
        subtitle: "Encryption, port ranges and DHT/PEX protocols",
      },
      {
        path: "/settings/seeding",
        title: "Seeding Rules",
        subtitle: "Ratio targets, seeding time and Hit & Run rules",
      },
      {
        path: "/settings/download-clients",
        title: "Download Clients",
        subtitle: "Connect qBittorrent, Transmission and Deluge",
      },
      {
        path: "/settings/indexers",
        title: "Indexer Settings",
        subtitle: "Prowlarr, Torznab and Jackett integrations",
      },
      {
        path: "/settings/connections",
        title: "Arr Connections",
        subtitle: "Sonarr, Radarr and Lidarr media enrichments",
      },
      {
        path: "/settings/network",
        title: "Network Settings",
        subtitle: "Proxy, IPv6, bound interface and DNS",
      },
      {
        path: "/settings/tracker-server",
        title: "Tracker Server Settings",
        subtitle: "Announce interval, scrape and whitelist controls",
      },
      {
        path: "/system/status",
        title: "System Status",
        subtitle: "Engine health, runtime stats and disk space",
      },
      {
        path: "/system/logs",
        title: "System Logs",
        subtitle: "Real-time backend log streaming and level filters",
      },
      {
        path: "/system/terminal",
        title: "System Terminal",
        subtitle: "Interactive web console and PTY terminal session",
      },
      {
        path: "/system/api",
        title: "API Reference (OpenAPI / Swagger)",
        subtitle: "Interactive REST API explorer and OpenAPI v3 schemas",
      },
    ];

    settingsSub.forEach((s) => {
      list.push({
        id: `set-${s.path}`,
        category: "Settings",
        title: s.title,
        subtitle: s.subtitle,
        icon: "⚙️",
        onSelect: () => {
          navigate(s.path);
          onClose();
        },
      });
    });

    // 3. Quick Actions
    list.push({
      id: "act-add",
      category: "Actions",
      title: "Add New Torrent",
      subtitle: "Add via .torrent file or magnet URI link",
      icon: "➕",
      onSelect: () => {
        onClose();
        onOpenAddTorrent?.();
      },
    });

    list.push({
      id: "act-pause-all",
      category: "Actions",
      title: "Pause All Seeding / Torrents",
      subtitle: "Pause all active torrent downloads and seeding swarms",
      icon: "⏸️",
      onSelect: () => {
        onClose();
        stopAll.mutate(undefined, {
          onSuccess: () =>
            showToast("All torrents and seeding swarms paused", "info"),
          onError: (err) => showToast(`Pause failed: ${err.message}`, "error"),
        });
      },
    });

    list.push({
      id: "act-resume-all",
      category: "Actions",
      title: "Resume All Seeding / Torrents",
      subtitle: "Resume seeding and downloading for all paused torrents",
      icon: "▶️",
      onSelect: () => {
        onClose();
        startAll.mutate(undefined, {
          onSuccess: () =>
            showToast("All torrents and seeding swarms resumed", "success"),
          onError: (err) => showToast(`Resume failed: ${err.message}`, "error"),
        });
      },
    });

    list.push({
      id: "act-turtle",
      category: "Actions",
      title: "Toggle Turtle Mode (Alternative Speed)",
      subtitle: seedingConfig?.alternativeSpeedEnabled
        ? "Currently active — switch back to normal speed limits"
        : "Currently disabled — switch to throttled alternate speed limits",
      icon: "🐢",
      badge: seedingConfig?.alternativeSpeedEnabled ? "ACTIVE" : undefined,
      badgeClass: "badge-warning",
      onSelect: () => {
        onClose();
        if (!seedingConfig) return;
        const nextState = !seedingConfig.alternativeSpeedEnabled;
        saveSeedingConfig.mutate(
          {
            ...seedingConfig,
            alternativeSpeedEnabled: nextState,
          },
          {
            onSuccess: () =>
              showToast(
                `Turtle mode ${nextState ? "enabled" : "disabled"}`,
                "info",
              ),
            onError: (err) =>
              showToast(
                `Turtle mode toggle failed: ${err.message}`,
                "error",
              ),
          },
        );
      },
    });

    list.push({
      id: "act-boost",
      category: "Actions",
      title: "Boost All Torrents (Verified Only)",
      subtitle:
        "Scrape candidate swarms and inject verified peers across downloads",
      icon: "⚡",
      onSelect: () => {
        onClose();
        boostAll.mutate(undefined, {
          onSuccess: (res) => {
            const count = Array.isArray(res)
              ? res.reduce(
                  (acc, r) =>
                    acc +
                    (r.addedTrackersCount || r.addedTrackers?.length || 0),
                  0,
                )
              : 0;
            showToast(
              `Swarm boost complete: ${count} trackers injected across swarms!`,
              "success",
            );
          },
          onError: (err) => showToast(`Boost failed: ${err.message}`, "error"),
        });
      },
    });

    list.push({
      id: "act-harvest",
      category: "Actions",
      title: "Harvest Trackers from Live Swarms",
      subtitle:
        "Discover new tracker endpoints from connected downloading clients",
      icon: "🔄",
      onSelect: () => {
        onClose();
        harvestSwarm.mutate(undefined, {
          onSuccess: (res) =>
            showToast(
              `Harvested ${res.harvestedCount ?? 0} new tracker endpoints!`,
              "success",
            ),
          onError: (err) =>
            showToast(`Harvest failed: ${err.message}`, "error"),
        });
      },
    });

    list.push({
      id: "act-prowlarr",
      category: "Actions",
      title: "Sync Trackers from Prowlarr Indexers",
      subtitle:
        "Extract public and configured trackers from all Prowlarr indexers",
      icon: "📡",
      onSelect: () => {
        onClose();
        syncProwlarr.mutate(undefined, {
          onSuccess: (res) =>
            showToast(
              `Synced ${res.harvestedCount ?? 0} trackers from Prowlarr!`,
              "success",
            ),
          onError: (err) => showToast(`Sync failed: ${err.message}`, "error"),
        });
      },
    });

    list.push({
      id: "act-probe",
      category: "Actions",
      title: "Probe Health of All Trackers",
      subtitle:
        "Ping all monitored tracker endpoints and update latency & status",
      icon: "🩺",
      onSelect: () => {
        onClose();
        scanTrackers.mutate(undefined, {
          onSuccess: (res) =>
            showToast(`Probed ${res.testedCount ?? 0} trackers!`, "success"),
          onError: (err) => showToast(`Probe failed: ${err.message}`, "error"),
        });
      },
    });

    list.push({
      id: "act-theme",
      category: "Actions",
      title: `Switch to ${theme === "dark" ? "Light" : "Dark"} Mode`,
      subtitle: `Toggle interface theme to ${theme === "dark" ? "light" : "dark"}`,
      icon: theme === "dark" ? "☀️" : "🌙",
      onSelect: () => {
        toggleTheme();
        onClose();
      },
    });

    list.push({
      id: "act-apikey",
      category: "Actions",
      title: "Copy API Key to Clipboard",
      subtitle: "Copy Seedarr API key for Arr or API integration",
      icon: "🔑",
      onSelect: async () => {
        try {
          let key = generalConfig?.apiKey;
          if (!key || key.includes("*")) {
            const res = await apiClient.getApiKey();
            key = res.apiKey;
          }
          if (key && !key.includes("*")) {
            await navigator.clipboard.writeText(key);
            showToast("API Key copied to clipboard!", "info");
          } else {
            showToast("No API Key available", "error");
          }
        } catch {
          showToast("Failed to copy API Key", "error");
        }
        onClose();
      },
    });

    list.push({
      id: "act-getting-started",
      category: "Actions",
      title: "Getting Started Guide & Setup",
      subtitle:
        "Open the onboarding walkthrough and connection setup guide (🚀)",
      icon: "🚀",
      onSelect: () => {
        onClose();
        onOpenGettingStarted?.();
      },
    });

    list.push({
      id: "act-shortcuts",
      category: "Actions",
      title: "Keyboard Shortcuts Cheat Sheet",
      subtitle: "View all keyboard shortcuts and navigation hotkeys (?)",
      icon: "⌨️",
      onSelect: () => {
        onClose();
        onOpenShortcuts?.();
      },
    });

    // 4. Live Torrents Search (Pre-indexed O(1) history lookups)
    const historyByHash = new Map<string, DownloadHistoryEntry>();
    const historyByTitle = new Map<string, DownloadHistoryEntry>();
    if (history) {
      for (const h of history) {
        if (h.infoHash) {
          historyByHash.set(h.infoHash.toLowerCase(), h);
        }
        if (h.title) {
          historyByTitle.set(h.title.toLowerCase(), h);
        }
      }
    }

    (torrents ?? []).forEach((t) => {
      const match =
        (t.infoHash ? historyByHash.get(t.infoHash.toLowerCase()) : undefined) ??
        (t.name ? historyByTitle.get(t.name.toLowerCase()) : undefined);
      const displayTitle = match?.metadata?.title || t.mediaTitle || t.name;

      list.push({
        id: `torrent-${t.id}`,
        category: "Torrents",
        title: displayTitle,
        subtitle: `${t.name} • ${formatBytes(t.totalSize)} • Ratio ${t.ratio.toFixed(2)} • ${t.status}`,
        icon:
          t.source === "Radarr"
            ? "🎬"
            : t.source === "Sonarr"
              ? "📺"
              : t.source === "Lidarr"
                ? "🎵"
                : "📦",
        posterUrl: match?.metadata?.posterUrl || t.posterUrl,
        badge: t.status,
        badgeClass:
          t.status === "Seeding"
            ? "badge-success"
            : t.status === "Stopped"
              ? "badge-danger"
              : "badge-primary",
        onSelect: () => {
          navigate(`/torrents?select=${t.id}`);
          onClose();
        },
      });
    });

    return list;
  }, [
    isOpen,
    torrents,
    history,
    generalConfig,
    theme,
    toggleTheme,
    navigate,
    onClose,
    onOpenShortcuts,
    onOpenAddTorrent,
    onOpenGettingStarted,
    boostAll,
    harvestSwarm,
    syncProwlarr,
    scanTrackers,
    startAll,
    stopAll,
    seedingConfig,
    saveSeedingConfig,
    showToast,
  ]);

  const filteredItems = useMemo(() => {
    return filterAndRankItems(items, deferredQuery, 30);
  }, [items, deferredQuery]);

  useEffect(() => {
    setSelectedIndex(0);
  }, [filteredItems]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Escape") {
      e.preventDefault();
      e.stopPropagation();
      e.nativeEvent?.stopImmediatePropagation?.();
      onClose();
      return;
    }

    if (filteredItems.length === 0) {
      return;
    }

    if (e.key === "ArrowDown") {
      e.preventDefault();
      isKeyboardNavigating.current = true;
      setSelectedIndex((prev) =>
        prev < filteredItems.length - 1 ? prev + 1 : 0,
      );
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      isKeyboardNavigating.current = true;
      setSelectedIndex((prev) =>
        prev > 0 ? prev - 1 : filteredItems.length - 1,
      );
    } else if (e.key === "Home") {
      e.preventDefault();
      isKeyboardNavigating.current = true;
      setSelectedIndex(0);
    } else if (e.key === "End") {
      e.preventDefault();
      isKeyboardNavigating.current = true;
      setSelectedIndex(filteredItems.length - 1);
    } else if (e.key === "PageDown") {
      e.preventDefault();
      isKeyboardNavigating.current = true;
      setSelectedIndex((prev) => Math.min(prev + 5, filteredItems.length - 1));
    } else if (e.key === "PageUp") {
      e.preventDefault();
      isKeyboardNavigating.current = true;
      setSelectedIndex((prev) => Math.max(prev - 5, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (filteredItems[selectedIndex]) {
        filteredItems[selectedIndex].onSelect();
      }
    }
  };

  useEffect(() => {
    const el = listRef.current?.children[selectedIndex] as HTMLElement;
    if (el) {
      el.scrollIntoView({ block: "nearest" });
    }
  }, [selectedIndex]);

  if (!isOpen) return null;

  return (
    <div
      ref={modalRef}
      role="dialog"
      aria-modal="true"
      aria-label="Command Palette"
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.75)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "flex-start",
        justifyContent: "center",
        paddingTop: "12vh",
        zIndex: 9999,
      }}
      onClick={onClose}
    >
      <div
        className="card"
        style={{
          width: "640px",
          maxWidth: "92vw",
          maxHeight: "75vh",
          padding: 0,
          display: "flex",
          flexDirection: "column",
          borderRadius: "12px",
          overflow: "hidden",
          border: "1px solid var(--border-light)",
          boxShadow: "0 16px 48px rgba(0, 0, 0, 0.6)",
        }}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        {/* Search header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.75rem",
            padding: "0.85rem 1.25rem",
            borderBottom: "1px solid var(--border-light)",
            backgroundColor: "var(--bg-secondary)",
          }}
        >
          <span style={{ fontSize: "1.2rem", opacity: 0.7 }}>🔍</span>
          <input
            ref={inputRef}
            type="text"
            className="form-control"
            placeholder="Type a command, page name, setting, or torrent title..."
            aria-label="Search commands, pages, and torrents"
            role="combobox"
            aria-expanded="true"
            aria-controls="command-palette-results"
            aria-autocomplete="list"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            style={{
              border: "none",
              backgroundColor: "transparent",
              fontSize: "1rem",
              padding: "0.25rem 0",
              boxShadow: "none",
            }}
          />
          <span
            className="badge badge-secondary"
            style={{
              fontSize: "0.7rem",
              padding: "0.2rem 0.5rem",
              fontFamily: "monospace",
            }}
          >
            ESC to close
          </span>
        </div>

        {/* Results List */}
        <div
          id="command-palette-results"
          role="listbox"
          ref={listRef}
          onMouseMove={(e) => {
            if (
              e.clientX !== lastPointerPos.current.x ||
              e.clientY !== lastPointerPos.current.y
            ) {
              lastPointerPos.current = { x: e.clientX, y: e.clientY };
              isKeyboardNavigating.current = false;
            }
          }}
          style={{
            overflowY: "auto",
            padding: "0.5rem",
            display: "flex",
            flexDirection: "column",
            gap: "0.2rem",
            maxHeight: "450px",
          }}
        >
          {filteredItems.length === 0 ? (
            <div
              style={{
                padding: "3rem 1rem",
                textAlign: "center",
                color: "var(--text-muted)",
              }}
            >
              No results found matching &quot;{query}&quot;
            </div>
          ) : (
            filteredItems.map((item, idx) => {
              const isSelected = idx === selectedIndex;
              return (
                <div
                  key={item.id}
                  role="option"
                  aria-selected={isSelected}
                  onClick={item.onSelect}
                  onMouseMove={(e) => {
                    if (
                      isKeyboardNavigating.current &&
                      e.clientX === lastPointerPos.current.x &&
                      e.clientY === lastPointerPos.current.y
                    ) {
                      return;
                    }
                    if (
                      e.clientX !== lastPointerPos.current.x ||
                      e.clientY !== lastPointerPos.current.y
                    ) {
                      lastPointerPos.current = { x: e.clientX, y: e.clientY };
                      isKeyboardNavigating.current = false;
                    }
                    if (selectedIndex !== idx) {
                      setSelectedIndex(idx);
                    }
                  }}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.75rem",
                    padding: "0.55rem 0.85rem",
                    borderRadius: "6px",
                    cursor: "pointer",
                    backgroundColor: isSelected
                      ? "var(--accent-glow, rgba(200, 168, 78, 0.15))"
                      : "transparent",
                    border: isSelected
                      ? "1px solid var(--accent, #c8a84e)"
                      : "1px solid transparent",
                    transition: "all 0.1s ease",
                  }}
                >
                  {item.posterUrl ? (
                    <MediaArtwork
                      src={item.posterUrl}
                      alt={item.title}
                      title={item.title}
                      category={item.category || undefined}
                      fallbackIcon={item.icon}
                      width="28px"
                      height="40px"
                      aspectRatio="auto"
                      borderRadius="3px"
                      style={{ flexShrink: 0 }}
                    />
                  ) : (
                    <span
                      style={{
                        fontSize: "1.2rem",
                        width: "28px",
                        textAlign: "center",
                        flexShrink: 0,
                      }}
                    >
                      {item.icon}
                    </span>
                  )}

                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "0.5rem",
                      }}
                    >
                      <span
                        style={{
                          fontWeight: 600,
                          fontSize: "0.88rem",
                          color: isSelected
                            ? "var(--text-primary)"
                            : "var(--text-primary)",
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}
                      >
                        {item.title}
                      </span>
                      {item.badge && (
                        <span
                          className={`badge ${item.badgeClass || "badge-secondary"}`}
                          style={{ fontSize: "0.65rem" }}
                        >
                          {item.badge}
                        </span>
                      )}
                    </div>
                    {item.subtitle && (
                      <div
                        style={{
                          fontSize: "0.74rem",
                          color: "var(--text-muted)",
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}
                      >
                        {item.subtitle}
                      </div>
                    )}
                  </div>

                  <span
                    className="badge badge-secondary"
                    style={{
                      fontSize: "0.65rem",
                      padding: "0.15rem 0.4rem",
                      opacity: 0.75,
                    }}
                  >
                    {item.category}
                  </span>
                </div>
              );
            })
          )}
        </div>

        {/* Footer shortcuts hint */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            padding: "0.5rem 1rem",
            backgroundColor: "var(--bg-secondary)",
            borderTop: "1px solid var(--border-light)",
            fontSize: "0.75rem",
            color: "var(--text-muted)",
          }}
        >
          <div style={{ display: "flex", gap: "1rem" }}>
            <span>
              <kbd>↑</kbd> <kbd>↓</kbd> Navigate
            </span>
            <span>
              <kbd>↵</kbd> Select
            </span>
            <span>
              <kbd>esc</kbd> Dismiss
            </span>
          </div>
          <span>Seedarr Quick Jump</span>
        </div>
      </div>
    </div>
  );
}

export default CommandPalette;
