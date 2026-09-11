import { useState, useEffect } from "react";
import {
  Routes,
  Route,
  NavLink,
  useLocation,
  useNavigate,
  Navigate,
  useParams,
} from "react-router";
import Dashboard from "./pages/Dashboard";
import TorrentIndex from "./pages/TorrentIndex";
import TorrentDetails from "./pages/TorrentDetails";
import Activity from "./pages/Activity";
import TrackerServer from "./pages/TrackerServer";
import Settings from "./pages/Settings";
import SystemStatus from "./pages/SystemStatus";
import SystemTasks from "./pages/SystemTasks";
import SystemLogs from "./pages/SystemLogs";
import SystemBackup from "./pages/SystemBackup";
import SystemUpdates from "./pages/SystemUpdates";
import SystemEvents from "./pages/SystemEvents";
import SystemLogFiles from "./pages/SystemLogFiles";
import PeerMap from "./pages/PeerMap";
import SpeedSchedule from "./pages/SpeedSchedule";
import Statistics from "./pages/Statistics";
import DownloadHistory from "./pages/DownloadHistory";
import TrackerBoost from "./pages/TrackerBoost";
import TrackerMetrics from "./pages/TrackerMetrics";
import Tags from "./pages/Tags";
import SystemNetwork from "./pages/SystemNetwork";
import ApiDocsPage from "./pages/ApiDocsPage";
import DownloadClientTorrents from "./pages/DownloadClientTorrents";
import StatusBar from "./components/StatusBar";
import ToastContainer from "./components/Toast";
import ErrorBoundary from "./components/ErrorBoundary";
import SignalRProvider from "./components/SignalRProvider";
import AddTorrentModal from "./components/AddTorrentModal";
import CommandPalette from "./components/CommandPalette";
import KeyboardShortcutsModal from "./components/KeyboardShortcutsModal";
import {
  GettingStartedModal,
  STORAGE_KEY_HIDE_GUIDE,
} from "./components/GettingStartedModal";
import AddTorrentPage from "./pages/AddTorrentPage";
import { useToast } from "./context/ToastContext";
import { apiClient } from "./api/client";
import SeedarrLogo from "./components/icons/SeedarrLogo";
import SeedarrText from "./components/icons/SeedarrText";
import {
  DashboardIcon,
  TorrentIcon,
  SettingsIcon,
  SystemIcon,
  DownloadAgentIcon,
} from "./components/icons/NavIcons";
import {
  ActivityIcon,
  MenuIcon,
  ChevronsLeftIcon,
  ChevronsRightIcon,
} from "./components/icons/UIIcons";
import {
  TrackerIcon,
  SunIcon,
  MoonIcon,
  HeartIcon,
  PeerMapIcon,
  ScheduleIcon,
  StatsIcon,
  HistoryIcon,
  SearchIcon,
} from "./components/icons/AppIcons";
import { useTheme } from "./context/ThemeContext";
import { useGeneralConfig, useDownloadClients } from "./api/hooks";
import { useTranslation } from "./i18n";
import LanguageSelector from "./components/LanguageSelector";
import { SETTINGS_GROUPS } from "./pages/settings/settingsNavData";

const systemSubItems = [
  { path: "/system/status", label: "Status" },
  { path: "/system/tasks", label: "Tasks" },
  { path: "/system/backup", label: "Backup" },
  { path: "/system/updates", label: "Updates" },
  { path: "/system/events", label: "Events" },
  { path: "/system/logfiles", label: "Log Files" },
  { path: "/system/network", label: "Network" },
  { path: "/system/api", label: "API Reference" },
];

function LegacyTorrentRedirect() {
  const { id } = useParams<{ id: string }>();
  return <Navigate to={id ? `/torrents/${id}` : "/torrents"} replace />;
}

function LegacyClientRedirect() {
  const { id } = useParams<{ id: string }>();
  return (
    <Navigate
      to={id ? `/activity/client/${id}` : "/activity/history"}
      replace
    />
  );
}

function App() {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const { theme, toggleTheme } = useTheme();
  const [showActionsMenu, setShowActionsMenu] = useState(false);
  const [showAddTorrentModal, setShowAddTorrentModal] = useState(false);
  const [showCommandPalette, setShowCommandPalette] = useState(false);
  const [showShortcutsModal, setShowShortcutsModal] = useState(false);
  const [showGettingStartedModal, setShowGettingStartedModal] =
    useState<boolean>(() => {
      if (typeof window !== "undefined" && window.navigator?.webdriver) {
        return false;
      }
      return localStorage.getItem(STORAGE_KEY_HIDE_GUIDE) !== "true";
    });
  const [openSettingsGroups, setOpenSettingsGroups] = useState<
    Record<string, boolean>
  >(() => {
    try {
      const saved = localStorage.getItem("seedarr_settings_accordion_state");
      return saved ? JSON.parse(saved) : {};
    } catch {
      return {};
    }
  });

  useEffect(() => {
    try {
      localStorage.setItem(
        "seedarr_settings_accordion_state",
        JSON.stringify(openSettingsGroups),
      );
    } catch {
      // ignore
    }
  }, [openSettingsGroups]);
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState<boolean>(() => {
    return localStorage.getItem("seedarr_sidebar_collapsed") === "true";
  });

  const toggleSidebar = () => {
    setIsSidebarCollapsed((prev) => {
      const next = !prev;
      localStorage.setItem("seedarr_sidebar_collapsed", String(next));
      return next;
    });
  };

  const isTorrentsRoute =
    (location.pathname.startsWith("/torrents") &&
      !location.pathname.startsWith("/torrents/history") &&
      !location.pathname.startsWith("/torrents/client")) ||
    location.pathname === "/add-torrent";
  const isActivityRoute =
    location.pathname.startsWith("/activity") ||
    location.pathname.startsWith("/torrents/history") ||
    location.pathname.startsWith("/torrents/client") ||
    location.pathname === "/history";
  const isTrackerRoute =
    location.pathname.startsWith("/tracker") ||
    location.pathname.startsWith("/trackerboost") ||
    location.pathname.startsWith("/trackermetrics") ||
    location.pathname.startsWith("/download++") ||
    location.pathname.startsWith("/downloadplusplus");
  const isSettingsRoute = location.pathname.startsWith("/settings");
  const isSystemRoute = location.pathname.startsWith("/system");
  const { data: generalConfig } = useGeneralConfig();
  const { data: downloadClients } = useDownloadClients();
  const { showToast } = useToast();
  const [showApiKey, setShowApiKey] = useState(false);
  const [unmaskedApiKey, setUnmaskedApiKey] = useState<string | null>(null);

  const fetchUnmaskedKey = async () => {
    if (unmaskedApiKey) return unmaskedApiKey;
    try {
      const res = await apiClient.getApiKey();
      if (res?.apiKey) {
        setUnmaskedApiKey(res.apiKey);
        return res.apiKey;
      }
    } catch {
      // Fallback
    }
    return generalConfig?.apiKey || "";
  };

  const handleCopyApiKey = async () => {
    try {
      let key = unmaskedApiKey;
      if (!key) {
        key = await fetchUnmaskedKey();
      }
      if (key && !key.includes("*")) {
        await navigator.clipboard.writeText(key);
        showToast(
          t("topbar.apiKeyCopied", undefined, "API Key copied to clipboard"),
          "success",
        );
      } else {
        showToast(
          t("topbar.failedToCopyApiKey", undefined, "Failed to copy API Key"),
          "error",
        );
      }
    } catch {
      showToast(
        t("topbar.failedToCopyApiKey", undefined, "Failed to copy API Key"),
        "error",
      );
    }
  };

  const handleMouseEnterApiKey = async () => {
    setShowApiKey(true);
    if (!unmaskedApiKey) {
      await fetchUnmaskedKey();
    }
  };

  const handleMouseLeaveApiKey = () => {
    setShowApiKey(false);
  };

  // Global Keyboard Shortcuts Listener
  useEffect(() => {
    let pendingGKey = false;
    let pendingGTimer: ReturnType<typeof setTimeout> | null = null;

    const handleKeyDown = (e: KeyboardEvent) => {
      const activeEl = document.activeElement;
      const isInputActive =
        activeEl &&
        (activeEl.tagName === "INPUT" ||
          activeEl.tagName === "TEXTAREA" ||
          activeEl.tagName === "SELECT" ||
          (activeEl as HTMLElement).isContentEditable);

      // Alt+M toggles sidebar collapse
      if (e.altKey && e.key.toLowerCase() === "m") {
        e.preventDefault();
        toggleSidebar();
        return;
      }

      // Cmd+K / Ctrl+K
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setShowCommandPalette((prev) => !prev);
        return;
      }

      // If typing inside an input/textarea, do not intercept single-key shortcuts
      if (isInputActive) return;

      // "/" opens search / command palette
      if (e.key === "/" && !e.ctrlKey && !e.metaKey) {
        e.preventDefault();
        setShowCommandPalette(true);
        return;
      }

      // "?" opens Keyboard Shortcuts cheat sheet
      if (e.key === "?" || (e.shiftKey && e.key === "/")) {
        e.preventDefault();
        setShowShortcutsModal(true);
        return;
      }

      // "g" sequence navigation (e.g. g then d => dashboard)
      if (e.key === "g" && !pendingGKey) {
        pendingGKey = true;
        if (pendingGTimer) clearTimeout(pendingGTimer);
        pendingGTimer = setTimeout(() => {
          pendingGKey = false;
        }, 1000);
        return;
      }

      if (pendingGKey) {
        pendingGKey = false;
        if (pendingGTimer) clearTimeout(pendingGTimer);

        if (e.key === "d") {
          e.preventDefault();
          navigate("/");
        } else if (e.key === "t") {
          e.preventDefault();
          navigate("/torrents");
        } else if (e.key === "h") {
          e.preventDefault();
          navigate("/activity/history");
        } else if (e.key === "b") {
          e.preventDefault();
          navigate("/tracker/trackerboost");
        } else if (e.key === "m") {
          e.preventDefault();
          navigate("/activity/metrics");
        } else if (e.key === "p") {
          e.preventDefault();
          navigate("/peermap");
        } else if (e.key === "s") {
          e.preventDefault();
          navigate("/settings/general");
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
      if (pendingGTimer) clearTimeout(pendingGTimer);
    };
  }, [navigate]);

  return (
    <div className={`app ${isSidebarCollapsed ? "sidebar-collapsed" : ""}`}>
      <aside className="sidebar" aria-label="Main Navigation">
        <div className="sidebar-header">
          <a
            href="https://www.seedarr.net"
            target="_blank"
            rel="noopener noreferrer"
            className="sidebar-logo"
            title="Seedarr"
            aria-label="Seedarr Homepage"
          >
            <SeedarrLogo size={isSidebarCollapsed ? 36 : 96} />
            {!isSidebarCollapsed && <SeedarrText width={140} />}
          </a>
          <button
            type="button"
            className="sidebar-toggle-btn"
            onClick={toggleSidebar}
            title={
              isSidebarCollapsed
                ? "Expand sidebar (Alt+M)"
                : "Collapse sidebar (Alt+M)"
            }
            aria-label={
              isSidebarCollapsed ? "Expand sidebar" : "Collapse sidebar"
            }
          >
            {isSidebarCollapsed ? (
              <ChevronsRightIcon size={14} />
            ) : (
              <ChevronsLeftIcon size={14} />
            )}
          </button>
        </div>
        <nav className="sidebar-nav">
          <NavLink
            to="/"
            end
            className="sidebar-nav-item"
            title={t("nav.dashboard", undefined, "Dashboard")}
          >
            <DashboardIcon />{" "}
            <span>{t("nav.dashboard", undefined, "Dashboard")}</span>
          </NavLink>

          {/* Torrents Top-Level with Live Torrents & Add Torrent */}
          <NavLink
            to="/torrents"
            className={`sidebar-nav-item ${isTorrentsRoute ? "active" : ""}`}
            title={t("nav.torrents", undefined, "Torrents")}
          >
            <TorrentIcon />{" "}
            <span>{t("nav.torrents", undefined, "Torrents")}</span>
          </NavLink>
          {isTorrentsRoute && (
            <>
              <NavLink
                to="/torrents"
                end
                className="sidebar-nav-item sidebar-nav-sub"
                title={t("nav.torrents", undefined, "Torrents")}
              >
                <TorrentIcon />{" "}
                <span>{t("nav.torrents", undefined, "Torrents")}</span>
              </NavLink>
              <NavLink
                to="/torrents/add"
                className="sidebar-nav-item sidebar-nav-sub"
                title={t("nav.addTorrent", undefined, "Add Torrent")}
              >
                <span style={{ fontSize: "1.1rem", lineHeight: 1 }}>+</span>{" "}
                <span>{t("nav.addTorrent", undefined, "Add Torrent")}</span>
              </NavLink>
            </>
          )}

          {/* Activity Top-Level with Historical Downloads, Download Agents, & Metrics */}
          <NavLink
            to="/activity/history"
            className={`sidebar-nav-item ${isActivityRoute ? "active" : ""}`}
            title={t("nav.activity", undefined, "Activity")}
          >
            <ActivityIcon />{" "}
            <span>{t("nav.activity", undefined, "Activity")}</span>
          </NavLink>
          {isActivityRoute && (
            <>
              <NavLink
                to="/activity/history"
                className="sidebar-nav-item sidebar-nav-sub"
                title={t("nav.history", undefined, "History")}
              >
                <HistoryIcon />{" "}
                <span>{t("nav.history", undefined, "History")}</span>
              </NavLink>
              {downloadClients
                ?.filter((c) => c.enable)
                .map((client) => (
                  <NavLink
                    key={client.id}
                    to={`/activity/client/${client.id}`}
                    className="sidebar-nav-item sidebar-nav-sub"
                    title={client.name}
                  >
                    <DownloadAgentIcon /> <span>{client.name}</span>
                  </NavLink>
                ))}
              <NavLink
                to="/activity/metrics"
                className="sidebar-nav-item sidebar-nav-sub"
                title={t("nav.metrics", undefined, "Metrics")}
              >
                <StatsIcon />{" "}
                <span>{t("nav.metrics", undefined, "Metrics")}</span>
              </NavLink>
            </>
          )}

          {/* Tracker Top-Level with Inbuilt and Boost */}
          <NavLink
            to="/tracker"
            className={`sidebar-nav-item ${isTrackerRoute ? "active" : ""}`}
            title={t("nav.tracker", undefined, "Tracker")}
          >
            <TrackerIcon />{" "}
            <span>{t("nav.tracker", undefined, "Tracker")}</span>
          </NavLink>
          {isTrackerRoute && (
            <>
              <NavLink
                to="/tracker/inbuilt"
                className={`sidebar-nav-item sidebar-nav-sub ${
                  location.pathname === "/tracker" ||
                  location.pathname === "/tracker/inbuilt"
                    ? "active"
                    : ""
                }`}
                title={t("nav.trackerInbuilt", undefined, "Inbuilt")}
              >
                <span>{t("nav.trackerInbuilt", undefined, "Inbuilt")}</span>
              </NavLink>
              <NavLink
                to="/tracker/trackerboost"
                className={`sidebar-nav-item sidebar-nav-sub ${
                  location.pathname === "/tracker/trackerboost" ||
                  location.pathname === "/tracker/boost" ||
                  location.pathname === "/trackerboost" ||
                  location.pathname === "/download++" ||
                  location.pathname === "/downloadplusplus"
                    ? "active"
                    : ""
                }`}
                title={t("nav.trackerBoost", undefined, "Tracker Boost")}
              >
                <span>{t("nav.trackerBoost", undefined, "Tracker Boost")}</span>
              </NavLink>
              <NavLink
                to="/tracker/metrics"
                className={`sidebar-nav-item sidebar-nav-sub ${
                  location.pathname === "/tracker/metrics" ||
                  location.pathname === "/trackermetrics"
                    ? "active"
                    : ""
                }`}
                title={t("nav.trackerMetrics", undefined, "Tracker Metrics")}
              >
                <span>
                  {t("nav.trackerMetrics", undefined, "Tracker Metrics")}
                </span>
              </NavLink>
            </>
          )}
          <NavLink
            to="/peermap"
            className="sidebar-nav-item"
            title={t("nav.peerMap", undefined, "Peer Map")}
          >
            <PeerMapIcon />{" "}
            <span>{t("nav.peerMap", undefined, "Peer Map")}</span>
          </NavLink>
          <NavLink
            to="/schedule"
            className="sidebar-nav-item"
            title={t("nav.schedule", undefined, "Schedule")}
          >
            <ScheduleIcon />{" "}
            <span>{t("nav.schedule", undefined, "Schedule")}</span>
          </NavLink>
          <NavLink
            to="/statistics"
            className="sidebar-nav-item"
            title={t("nav.statistics", undefined, "Statistics")}
          >
            <StatsIcon />{" "}
            <span>{t("nav.statistics", undefined, "Statistics")}</span>
          </NavLink>
          <NavLink
            to="/settings/general"
            className={`sidebar-nav-item ${isSettingsRoute ? "active-parent active" : ""}`}
            title={t("nav.settings", undefined, "Settings")}
          >
            <SettingsIcon />{" "}
            <span>{t("nav.settings", undefined, "Settings")}</span>
          </NavLink>
          {isSettingsRoute && (
            <div className="sidebar-settings-tree">
              {SETTINGS_GROUPS.map((group) => {
                const isGroupActive = group.pages.some(
                  (p) =>
                    location.pathname === `/settings/${p.id}` ||
                    (p.id === "general" &&
                      (location.pathname === "/settings" ||
                        location.pathname === "/settings/general")),
                );
                const isOpen = openSettingsGroups[group.id] ?? isGroupActive;
                return (
                  <div key={group.id} className="sidebar-group-container">
                    <div
                      className="sidebar-group-header"
                      onClick={(e) => {
                        e.stopPropagation();
                        setOpenSettingsGroups((prev) => ({
                          ...prev,
                          [group.id]: !isOpen,
                        }));
                      }}
                      title={`Toggle ${group.title}`}
                    >
                      <span
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: "0.4rem",
                        }}
                      >
                        <span>{group.icon}</span>
                        <span>{group.shortLabel}</span>
                      </span>
                      <span
                        className={`sidebar-group-chevron ${isOpen ? "open" : ""}`}
                      >
                        ▶
                      </span>
                    </div>
                    {isOpen &&
                      group.pages.map((page) => {
                        const isPageActive =
                          location.pathname === `/settings/${page.id}` ||
                          (page.id === "general" &&
                            (location.pathname === "/settings" ||
                              location.pathname === "/settings/general"));
                        return (
                          <NavLink
                            key={page.id}
                            to={`/settings/${page.id}`}
                            className={`sidebar-settings-subitem sidebar-nav-sub ${isPageActive ? "active" : ""}`}
                            title={page.description}
                          >
                            <span
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "0.45rem",
                                overflow: "hidden",
                                textOverflow: "ellipsis",
                                whiteSpace: "nowrap",
                              }}
                            >
                              <span
                                style={{
                                  fontSize: "0.85rem",
                                  flexShrink: 0,
                                }}
                              >
                                {page.icon}
                              </span>
                              <span
                                style={{
                                  overflow: "hidden",
                                  textOverflow: "ellipsis",
                                }}
                              >
                                {page.shortLabel}
                              </span>
                            </span>
                            {page.badge && (
                              <span
                                className="sidebar-badge"
                                style={{
                                  backgroundColor: isPageActive
                                    ? "var(--accent)"
                                    : "rgba(255,255,255,0.06)",
                                  color: isPageActive
                                    ? "#10111a"
                                    : "var(--text-muted)",
                                }}
                              >
                                {page.badge}
                              </span>
                            )}
                          </NavLink>
                        );
                      })}
                  </div>
                );
              })}
            </div>
          )}
          <NavLink
            to="/system/status"
            className={`sidebar-nav-item ${isSystemRoute ? "active" : ""}`}
            title={t("nav.system", undefined, "System")}
          >
            <SystemIcon /> <span>{t("nav.system", undefined, "System")}</span>
          </NavLink>
          {isSystemRoute &&
            systemSubItems.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className="sidebar-nav-item sidebar-nav-sub"
                title={item.label}
              >
                <span>{item.label}</span>
              </NavLink>
            ))}
        </nav>
      </aside>

      <div className="main-wrapper">
        <header className="topbar">
          <div
            className="topbar-left"
            style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
          >
            <button
              type="button"
              className="topbar-btn topbar-sidebar-toggle"
              onClick={toggleSidebar}
              title={
                isSidebarCollapsed
                  ? "Expand sidebar (Alt+M)"
                  : "Collapse sidebar (Alt+M)"
              }
              aria-label="Toggle navigation sidebar"
              style={{
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                width: "28px",
                height: "28px",
                border: "1px solid var(--border-light, #3a352e)",
                borderRadius: "4px",
                background: "transparent",
                color: "var(--text-secondary)",
                cursor: "pointer",
                fontSize: "0.95rem",
                padding: 0,
              }}
            >
              <MenuIcon size={16} />
            </button>
            <div
              className="topbar-search"
              onClick={() => setShowCommandPalette(true)}
              style={{ cursor: "pointer" }}
              title={t(
                "topbar.searchPlaceholder",
                undefined,
                "Quick Jump / Search... (Ctrl+K)",
              )}
            >
              <SearchIcon size={14} />
              <input
                type="text"
                placeholder={t(
                  "topbar.searchPlaceholder",
                  undefined,
                  "Quick Jump / Search... (Ctrl+K)",
                )}
                className="topbar-search-input"
                value=""
                readOnly
                onClick={() => setShowCommandPalette(true)}
                aria-label={t(
                  "topbar.searchPlaceholder",
                  undefined,
                  "Quick Jump / Search... (Ctrl+K)",
                )}
                style={{ cursor: "pointer" }}
              />
              <kbd
                style={{
                  backgroundColor: "rgba(255, 255, 255, 0.08)",
                  border: "1px solid rgba(255, 255, 255, 0.16)",
                  borderRadius: "3px",
                  padding: "0.1rem 0.4rem",
                  fontSize: "0.7rem",
                  color: "var(--text-muted)",
                  fontFamily: "monospace",
                }}
              >
                Ctrl+K
              </kbd>
            </div>
          </div>
          <div
            className="topbar-actions"
            style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}
          >
            <LanguageSelector />
            <button
              className="btn btn-small"
              onClick={() => setShowGettingStartedModal(true)}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.4rem",
                backgroundColor:
                  "var(--accent-bg-medium, rgba(200, 168, 78, 0.12))",
                color: "var(--accent, #c8a84e)",
                border:
                  "1px solid var(--accent-border-alert, rgba(200, 168, 78, 0.3))",
                fontWeight: 600,
              }}
              title={t("nav.gettingStarted", undefined, "Getting Started")}
              aria-label={t("nav.gettingStarted", undefined, "Getting Started Guide")}
            >
              🚀 {t("nav.gettingStarted", undefined, "Getting Started")}
            </button>
            {generalConfig?.apiKey && (
              <button
                type="button"
                className="topbar-apikey-btn"
                onClick={handleCopyApiKey}
                onMouseEnter={handleMouseEnterApiKey}
                onMouseLeave={handleMouseLeaveApiKey}
                title={t(
                  "topbar.copyApiKey",
                  undefined,
                  "Click to copy API Key to clipboard",
                )}
                aria-label={t(
                  "topbar.copyApiKey",
                  undefined,
                  "Click to copy API Key to clipboard",
                )}
              >
                <span style={{ fontSize: "0.85rem" }}>⚿</span>
                <span
                  style={{
                    letterSpacing: showApiKey ? "0.5px" : "1px",
                    opacity: 0.85,
                    fontFamily: "monospace",
                    fontSize: "0.8rem",
                  }}
                >
                  {showApiKey
                    ? (unmaskedApiKey || (generalConfig.apiKey.includes("*") ? "••••••••••••••••••••••••••••••••" : generalConfig.apiKey))
                    : "••••••••••••••••••••••••••••••••"}
                </span>
              </button>
            )}
            <button
              className="topbar-btn"
              onClick={toggleTheme}
              title={
                theme === "dark"
                  ? t("topbar.switchLight", undefined, "Switch to light theme")
                  : t("topbar.switchDark", undefined, "Switch to dark theme")
              }
              aria-label={
                theme === "dark"
                  ? t("topbar.switchLight", undefined, "Switch to light theme")
                  : t("topbar.switchDark", undefined, "Switch to dark theme")
              }
            >
              {theme === "dark" ? <SunIcon /> : <MoonIcon />}
            </button>
            <a
              className="topbar-btn topbar-heart"
              href="https://github.com/sponsors/dmzoneill"
              target="_blank"
              rel="noopener noreferrer"
              title={t("topbar.supportSeedarr", undefined, "Support Seedarr")}
              aria-label={t("topbar.supportSeedarr", undefined, "Support Seedarr")}
            >
              <HeartIcon />
            </a>
            <div style={{ position: "relative" }}>
              <button
                className="topbar-btn"
                onClick={() => setShowActionsMenu(!showActionsMenu)}
                title={t("topbar.actions", undefined, "Actions")}
                aria-label={t("topbar.actions", undefined, "Actions menu")}
                aria-expanded={showActionsMenu}
                aria-haspopup="menu"
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  justifyContent: "center",
                  padding: "2px",
                }}
              >
                <div
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    justifyContent: "center",
                    width: "24px",
                    height: "24px",
                    borderRadius: "50%",
                    backgroundColor: "var(--bg-hover-elevated, #4a4438)",
                    color: "var(--accent, #c8a84e)",
                    fontSize: "11px",
                    fontWeight: 700,
                    border: "1px solid var(--border, #3a352e)",
                    overflow: "hidden",
                  }}
                >
                  A
                </div>
              </button>
              {showActionsMenu && (
                <div
                  className="topbar-dropdown"
                  role="menu"
                  onClick={() => setShowActionsMenu(false)}
                >
                  <button
                    className="topbar-dropdown-item"
                    role="menuitem"
                    onClick={() => navigate("/system/status")}
                  >
                    {t("topbar.systemStatus", undefined, "System Status")}
                  </button>
                  <button
                    className="topbar-dropdown-item"
                    role="menuitem"
                    onClick={() => navigate("/settings/general")}
                  >
                    {t("topbar.settings", undefined, "Settings")}
                  </button>
                  <button
                    className="topbar-dropdown-item"
                    role="menuitem"
                    onClick={() => setShowCommandPalette(true)}
                  >
                    {t(
                      "topbar.commandPalette",
                      undefined,
                      "🔍 Command Palette (⌘K / Ctrl+K)",
                    )}
                  </button>
                  <button
                    className="topbar-dropdown-item"
                    role="menuitem"
                    onClick={() => setShowShortcutsModal(true)}
                  >
                    {t(
                      "topbar.keyboardShortcuts",
                      undefined,
                      "⌨️ Keyboard Shortcuts (?)",
                    )}
                  </button>
                  <button
                    className="topbar-dropdown-item"
                    role="menuitem"
                    onClick={() => setShowGettingStartedModal(true)}
                  >
                    {t(
                      "topbar.gettingStarted",
                      undefined,
                      "🚀 Getting Started Guide",
                    )}
                  </button>
                  <div className="topbar-dropdown-separator" />
                  <button
                    className="topbar-dropdown-item"
                    role="menuitem"
                    onClick={() => {
                      if (
                        confirm(
                          t(
                            "topbar.restartConfirm",
                            undefined,
                            "Restart Seedarr?",
                          ),
                        )
                      ) {
                        apiClient
                          .post("/system/restart")
                          .catch((err) => {
                            console.error("System action failed:", err);
                            showToast(
                              t(
                                "topbar.actionFailed",
                                undefined,
                                "System action failed",
                              ),
                              "error",
                            );
                          });
                      }
                    }}
                  >
                    {t("topbar.restart", undefined, "Restart")}
                  </button>
                  <button
                    className="topbar-dropdown-item topbar-dropdown-danger"
                    role="menuitem"
                    onClick={() => {
                      if (
                        confirm(
                          t(
                            "topbar.shutdownConfirm",
                            undefined,
                            "Shut down Seedarr?",
                          ),
                        )
                      ) {
                        apiClient
                          .post("/system/shutdown")
                          .catch((err) => {
                            console.error("System action failed:", err);
                            showToast(
                              t(
                                "topbar.actionFailed",
                                undefined,
                                "System action failed",
                              ),
                              "error",
                            );
                          });
                      }
                    }}
                  >
                    {t("topbar.shutdown", undefined, "Shutdown")}
                  </button>
                </div>
              )}
            </div>
          </div>
        </header>

        <ToastContainer />
        <main className="app-main">
          <ErrorBoundary>
            <Routes>
              <Route path="/" element={<Dashboard />} />
              <Route path="/torrents" element={<TorrentIndex />} />
              <Route path="/torrents/add" element={<AddTorrentPage />} />
              <Route
                path="/add-torrent"
                element={<Navigate to="/torrents/add" replace />}
              />
              <Route path="/torrents/:id" element={<TorrentDetails />} />

              <Route
                path="/activity"
                element={<Navigate to="/activity/history" replace />}
              />
              <Route path="/activity/history" element={<DownloadHistory />} />
              <Route path="/activity/metrics" element={<Activity />} />
              <Route
                path="/activity/client/:id"
                element={<DownloadClientTorrents />}
              />

              {/* Legacy route redirects */}
              <Route
                path="/activity/torrents"
                element={<Navigate to="/torrents" replace />}
              />
              <Route
                path="/activity/torrents/:id"
                element={<LegacyTorrentRedirect />}
              />
              <Route
                path="/torrents/history"
                element={<Navigate to="/activity/history" replace />}
              />
              <Route
                path="/history"
                element={<Navigate to="/activity/history" replace />}
              />
              <Route
                path="/torrents/client/:id"
                element={<LegacyClientRedirect />}
              />
              <Route path="/downloadplusplus" element={<TrackerBoost />} />
              <Route path="/download++" element={<TrackerBoost />} />
              <Route path="/trackerboost" element={<TrackerBoost />} />
              <Route path="/tracker" element={<TrackerServer />} />
              <Route path="/tracker/inbuilt" element={<TrackerServer />} />
              <Route path="/tracker/trackerboost" element={<TrackerBoost />} />
              <Route path="/tracker/boost" element={<TrackerBoost />} />
              <Route path="/tracker/metrics" element={<TrackerMetrics />} />
              <Route path="/trackermetrics" element={<TrackerMetrics />} />
              <Route path="/peermap" element={<PeerMap />} />
              <Route path="/schedule" element={<SpeedSchedule />} />
              <Route path="/statistics" element={<Statistics />} />
              <Route path="/settings/tags" element={<Tags />} />
              <Route path="/settings/:section?" element={<Settings />} />
              <Route path="/system/status" element={<SystemStatus />} />
              <Route path="/system/tasks" element={<SystemTasks />} />
              <Route path="/system/logs" element={<SystemLogs />} />
              <Route path="/system/backup" element={<SystemBackup />} />
              <Route path="/system/updates" element={<SystemUpdates />} />
              <Route path="/system/events" element={<SystemEvents />} />
              <Route path="/system/logfiles" element={<SystemLogFiles />} />
              <Route path="/system/network" element={<SystemNetwork />} />
              <Route path="/system/api" element={<ApiDocsPage />} />
              <Route path="/system/swagger" element={<ApiDocsPage />} />
              <Route path="/api-docs" element={<ApiDocsPage />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </ErrorBoundary>
        </main>
        <StatusBar />
      </div>
      <SignalRProvider />
      {showAddTorrentModal && (
        <AddTorrentModal onClose={() => setShowAddTorrentModal(false)} />
      )}
      <CommandPalette
        isOpen={showCommandPalette}
        onClose={() => setShowCommandPalette(false)}
        onOpenShortcuts={() => setShowShortcutsModal(true)}
        onOpenAddTorrent={() => setShowAddTorrentModal(true)}
        onOpenGettingStarted={() => setShowGettingStartedModal(true)}
      />
      <KeyboardShortcutsModal
        isOpen={showShortcutsModal}
        onClose={() => setShowShortcutsModal(false)}
      />
      <GettingStartedModal
        isOpen={showGettingStartedModal}
        onClose={() => setShowGettingStartedModal(false)}
      />
    </div>
  );
}

export default App;
