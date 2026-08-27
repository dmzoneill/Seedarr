import { useState, useEffect, useRef, useMemo } from "react";
import { useNavigate } from "react-router";
import {
  useTorrents,
  useDownloadHistory,
  useBoostAllTorrents,
  useHarvestDownloadTrackers,
  useHarvestProwlarrTrackers,
  useScanTrackerBoostTrackers,
  useGeneralConfig,
} from "../api/hooks";
import { useTheme } from "../context/ThemeContext";
import { useToast } from "../context/ToastContext";
import { formatBytes } from "../utils/formatters";

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
}

export function CommandPalette({
  isOpen,
  onClose,
  onOpenShortcuts,
  onOpenAddTorrent,
}: CommandPaletteProps) {
  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  const { data: torrents } = useTorrents();
  const { data: history } = useDownloadHistory();
  const { data: generalConfig } = useGeneralConfig();
  const { theme, toggleTheme } = useTheme();
  const { showToast } = useToast();

  const boostAll = useBoostAllTorrents();
  const harvestSwarm = useHarvestDownloadTrackers();
  const syncProwlarr = useHarvestProwlarrTrackers();
  const scanTrackers = useScanTrackerBoostTrackers();

  useEffect(() => {
    if (isOpen) {
      setQuery("");
      setSelectedIndex(0);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

  const items = useMemo<CommandItem[]>(() => {
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
        path: "/torrents/history",
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
      id: "act-boost",
      category: "Actions",
      title: "Boost All Torrents (Verified Only)",
      subtitle:
        "Scrape candidate swarms and inject verified peers across downloads",
      icon: "⚡",
      onSelect: () => {
        onClose();
        boostAll.mutate(undefined, {
          onSuccess: (res) =>
            showToast(
              `Swarm boost complete: ${res.totalInjected} trackers injected!`,
              "success",
            ),
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
              `Harvested ${res.discoveredCount} new tracker endpoints!`,
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
              `Synced ${res.discoveredCount} trackers from Prowlarr!`,
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
            showToast(
              `Probed ${res.totalScanned} trackers (${res.aliveCount} alive)!`,
              "success",
            ),
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
      onSelect: () => {
        if (generalConfig?.apiKey) {
          navigator.clipboard.writeText(generalConfig.apiKey);
          showToast("API Key copied to clipboard!", "info");
        }
        onClose();
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

    // 4. Live Torrents Search
    (torrents ?? []).forEach((t) => {
      const match = (history ?? []).find(
        (h) =>
          (t.infoHash &&
            h.infoHash?.toLowerCase() === t.infoHash.toLowerCase()) ||
          h.title?.toLowerCase() === t.name?.toLowerCase(),
      );
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
    torrents,
    history,
    generalConfig,
    theme,
    toggleTheme,
    navigate,
    onClose,
    onOpenShortcuts,
    onOpenAddTorrent,
    boostAll,
    harvestSwarm,
    syncProwlarr,
    scanTrackers,
    showToast,
  ]);

  const filteredItems = useMemo(() => {
    if (!query.trim()) return items.slice(0, 30);
    const q = query.toLowerCase();
    return items
      .filter(
        (i) =>
          i.title.toLowerCase().includes(q) ||
          (i.subtitle && i.subtitle.toLowerCase().includes(q)) ||
          i.category.toLowerCase().includes(q),
      )
      .slice(0, 30);
  }, [items, query]);

  useEffect(() => {
    setSelectedIndex(0);
  }, [filteredItems]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((prev) =>
        prev < filteredItems.length - 1 ? prev + 1 : 0,
      );
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((prev) =>
        prev > 0 ? prev - 1 : filteredItems.length - 1,
      );
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (filteredItems[selectedIndex]) {
        filteredItems[selectedIndex].onSelect();
      }
    } else if (e.key === "Escape") {
      e.preventDefault();
      onClose();
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
          border: "1px solid rgba(255, 255, 255, 0.16)",
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
          ref={listRef}
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
                  onClick={item.onSelect}
                  onMouseEnter={() => setSelectedIndex(idx)}
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
                    <img
                      src={item.posterUrl}
                      alt=""
                      style={{
                        width: "28px",
                        height: "40px",
                        borderRadius: "3px",
                        objectFit: "cover",
                        flexShrink: 0,
                      }}
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
