import { useState, useEffect } from "react";
import { Routes, Route, NavLink, useLocation, useNavigate } from "react-router";
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
import Tags from "./pages/Tags";
import SystemNetwork from "./pages/SystemNetwork";
import DownloadClientTorrents from "./pages/DownloadClientTorrents";
import StatusBar from "./components/StatusBar";
import ToastContainer from "./components/Toast";
import ErrorBoundary from "./components/ErrorBoundary";
import SignalRProvider from "./components/SignalRProvider";
import AddTorrentModal from "./components/AddTorrentModal";
import CommandPalette from "./components/CommandPalette";
import KeyboardShortcutsModal from "./components/KeyboardShortcutsModal";
import AddTorrentPage from "./pages/AddTorrentPage";
import SeedarrLogo from "./components/icons/SeedarrLogo";
import SeedarrText from "./components/icons/SeedarrText";
import {
  DashboardIcon,
  TorrentIcon,
  SettingsIcon,
  SystemIcon,
  DownloadAgentIcon,
} from "./components/icons/NavIcons";
import { ActivityIcon } from "./components/icons/UIIcons";
import {
  TrackerIcon,
  SunIcon,
  MoonIcon,
  HeartIcon,
  UserIcon,
  PeerMapIcon,
  ScheduleIcon,
  StatsIcon,
  HistoryIcon,
  SearchIcon,
  KeyIcon,
} from "./components/icons/AppIcons";
import { useTheme } from "./context/ThemeContext";
import { apiClient } from "./api/client";
import { useGeneralConfig, useDownloadClients } from "./api/hooks";

const systemSubItems = [
  { path: "/system/status", label: "Status" },
  { path: "/system/tasks", label: "Tasks" },
  { path: "/system/backup", label: "Backup" },
  { path: "/system/updates", label: "Updates" },
  { path: "/system/events", label: "Events" },
  { path: "/system/logfiles", label: "Log Files" },
  { path: "/system/network", label: "Network" },
];

const settingsSubItems = [
  { path: "/settings/general", label: "General" },
  { path: "/settings/webui", label: "Web UI" },
  { path: "/settings/notifications", label: "Notifications" },
  { path: "/settings/seeding", label: "Seeding" },
  { path: "/settings/bittorrent", label: "BitTorrent" },
  { path: "/settings/network", label: "Network" },
  { path: "/settings/peer-protocol", label: "Peer Protocol" },
  { path: "/settings/protocols", label: "Protocols" },
  { path: "/settings/simulation", label: "Simulation" },
  { path: "/settings/tracker-server", label: "Tracker Server" },
  { path: "/settings/scheduler", label: "Scheduler" },
  { path: "/settings/indexers", label: "Indexers" },
  { path: "/settings/connections", label: "Connections" },
  { path: "/settings/download-clients", label: "Download Clients" },
  { path: "/settings/tags", label: "Tags" },
  { path: "/settings/advanced", label: "Advanced" },
];

function App() {
  const location = useLocation();
  const navigate = useNavigate();
  const { theme, toggleTheme } = useTheme();
  const [searchTerm, setSearchTerm] = useState("");
  const [showActionsMenu, setShowActionsMenu] = useState(false);
  const [showAddTorrentModal, setShowAddTorrentModal] = useState(false);
  const [showCommandPalette, setShowCommandPalette] = useState(false);
  const [showShortcutsModal, setShowShortcutsModal] = useState(false);

  const isTorrentsRoute =
    location.pathname.startsWith("/torrents") ||
    location.pathname === "/history";
  const isActivityRoute = location.pathname.startsWith("/activity");
  const isTrackerRoute =
    location.pathname.startsWith("/tracker") ||
    location.pathname.startsWith("/trackerboost") ||
    location.pathname.startsWith("/download++") ||
    location.pathname.startsWith("/downloadplusplus");
  const isSettingsRoute = location.pathname.startsWith("/settings");
  const isSystemRoute = location.pathname.startsWith("/system");
  const { data: generalConfig } = useGeneralConfig();
  const { data: downloadClients } = useDownloadClients();
  const [showApiKey, setShowApiKey] = useState(false);

  // Global Keyboard Shortcuts Listener
  useEffect(() => {
    let pendingGKey = false;
    let pendingGTimer: any = null;

    const handleKeyDown = (e: KeyboardEvent) => {
      const activeEl = document.activeElement;
      const isInputActive =
        activeEl &&
        (activeEl.tagName === "INPUT" ||
          activeEl.tagName === "TEXTAREA" ||
          activeEl.tagName === "SELECT" ||
          (activeEl as HTMLElement).isContentEditable);

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
        clearTimeout(pendingGTimer);
        pendingGTimer = setTimeout(() => {
          pendingGKey = false;
        }, 1000);
        return;
      }

      if (pendingGKey) {
        pendingGKey = false;
        clearTimeout(pendingGTimer);

        if (e.key === "d") {
          e.preventDefault();
          navigate("/");
        } else if (e.key === "t") {
          e.preventDefault();
          navigate("/torrents");
        } else if (e.key === "h") {
          e.preventDefault();
          navigate("/torrents/history");
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
      clearTimeout(pendingGTimer);
    };
  }, [navigate]);

  return (
    <div className="app">
      <aside className="sidebar">
        <a
          href="https://www.seedarr.net"
          target="_blank"
          rel="noopener noreferrer"
          className="sidebar-logo"
        >
          <SeedarrLogo size={96} />
          <SeedarrText width={140} />
        </a>
        <nav className="sidebar-nav">
          <NavLink to="/" end className="sidebar-nav-item">
            <DashboardIcon /> <span>Dashboard</span>
          </NavLink>

          {/* Torrents Top-Level with Historical History & Add Torrent */}
          <NavLink
            to="/torrents"
            className={`sidebar-nav-item ${isTorrentsRoute ? "active" : ""}`}
          >
            <TorrentIcon /> <span>Torrents</span>
          </NavLink>
          {isTorrentsRoute && (
            <>
              <NavLink
                to="/torrents/history"
                className="sidebar-nav-item sidebar-nav-sub"
              >
                <HistoryIcon /> <span>History</span>
              </NavLink>
              <NavLink
                to="/torrents/add"
                className="sidebar-nav-item sidebar-nav-sub"
              >
                <span style={{ fontSize: "1.1rem", lineHeight: 1 }}>+</span>{" "}
                <span>Add Torrent</span>
              </NavLink>
            </>
          )}

          {/* Activity Top-Level with Active Torrents, Download Agents, & Metrics */}
          <NavLink
            to="/activity/torrents"
            className={`sidebar-nav-item ${isActivityRoute ? "active" : ""}`}
          >
            <ActivityIcon /> <span>Activity</span>
          </NavLink>
          {isActivityRoute && (
            <>
              <NavLink
                to="/activity/torrents"
                className="sidebar-nav-item sidebar-nav-sub"
              >
                <DashboardIcon /> <span>Torrents</span>
              </NavLink>
              {downloadClients
                ?.filter((c) => c.enable)
                .map((client) => (
                  <NavLink
                    key={client.id}
                    to={`/activity/client/${client.id}`}
                    className="sidebar-nav-item sidebar-nav-sub"
                  >
                    <DownloadAgentIcon /> <span>{client.name}</span>
                  </NavLink>
                ))}
              <NavLink
                to="/activity/metrics"
                className="sidebar-nav-item sidebar-nav-sub"
              >
                <StatsIcon /> <span>Metrics</span>
              </NavLink>
            </>
          )}

          {/* Tracker Top-Level with Inbuilt and Boost */}
          <NavLink
            to="/tracker"
            className={`sidebar-nav-item ${isTrackerRoute ? "active" : ""}`}
          >
            <TrackerIcon /> <span>Tracker</span>
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
              >
                <span>Inbuilt</span>
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
              >
                <span>Tracker Boost</span>
              </NavLink>
            </>
          )}
          <NavLink to="/peermap" className="sidebar-nav-item">
            <PeerMapIcon /> <span>Peer Map</span>
          </NavLink>
          <NavLink to="/schedule" className="sidebar-nav-item">
            <ScheduleIcon /> <span>Schedule</span>
          </NavLink>
          <NavLink to="/statistics" className="sidebar-nav-item">
            <StatsIcon /> <span>Statistics</span>
          </NavLink>
          <NavLink
            to="/settings/general"
            className={`sidebar-nav-item ${isSettingsRoute ? "active" : ""}`}
          >
            <SettingsIcon /> <span>Settings</span>
          </NavLink>
          {isSettingsRoute &&
            settingsSubItems.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className="sidebar-nav-item sidebar-nav-sub"
              >
                <span>{item.label}</span>
              </NavLink>
            ))}
          <NavLink
            to="/system/status"
            className={`sidebar-nav-item ${isSystemRoute ? "active" : ""}`}
          >
            <SystemIcon /> <span>System</span>
          </NavLink>
          {isSystemRoute &&
            systemSubItems.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className="sidebar-nav-item sidebar-nav-sub"
              >
                <span>{item.label}</span>
              </NavLink>
            ))}
        </nav>
      </aside>

      <div className="main-wrapper">
        <header className="topbar">
          <div
            className="topbar-search"
            onClick={() => setShowCommandPalette(true)}
            style={{
              cursor: "pointer",
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
            title="Search & Quick Jump (Ctrl+K or /)"
          >
            <SearchIcon />
            <input
              type="text"
              placeholder="Quick Jump / Search... (Ctrl+K or /)"
              className="topbar-search-input"
              value={searchTerm}
              readOnly
              onClick={() => setShowCommandPalette(true)}
              style={{ cursor: "pointer" }}
            />
            <kbd
              style={{
                backgroundColor: "rgba(255, 255, 255, 0.08)",
                border: "1px solid rgba(255, 255, 255, 0.16)",
                borderRadius: "4px",
                padding: "0.1rem 0.4rem",
                fontSize: "0.7rem",
                color: "var(--text-muted)",
                fontFamily: "monospace",
                marginRight: "0.4rem",
              }}
            >
              ⌘K
            </kbd>
          </div>
          <div className="topbar-actions">
            {generalConfig?.apiKey && (
              <div
                className="topbar-api-key"
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                  marginRight: "1rem",
                  color: "var(--text-dim)",
                  fontSize: "0.85rem",
                }}
              >
                <KeyIcon size={14} />
                <code
                  style={{
                    background: "var(--bg-lighter)",
                    padding: "0.2rem 0.5rem",
                    borderRadius: "4px",
                    cursor: "pointer",
                    userSelect: "none",
                  }}
                  onClick={() => {
                    navigator.clipboard.writeText(generalConfig.apiKey);
                  }}
                  onMouseEnter={() => setShowApiKey(true)}
                  onMouseLeave={() => setShowApiKey(false)}
                  title="Click to copy API Key"
                >
                  {showApiKey
                    ? generalConfig.apiKey
                    : "••••••••••••••••••••••••••••••••"}
                </code>
              </div>
            )}
            <button
              className="topbar-btn"
              onClick={toggleTheme}
              title={
                theme === "dark"
                  ? "Switch to light theme"
                  : "Switch to dark theme"
              }
            >
              {theme === "dark" ? <SunIcon /> : <MoonIcon />}
            </button>
            <a
              className="topbar-btn topbar-heart"
              href="https://github.com/sponsors/dmzoneill"
              target="_blank"
              rel="noopener noreferrer"
              title="Support Seedarr"
            >
              <HeartIcon />
            </a>
            <div style={{ position: "relative" }}>
              <button
                className="topbar-btn"
                onClick={() => setShowActionsMenu(!showActionsMenu)}
                title="Actions"
              >
                <UserIcon />
              </button>
              {showActionsMenu && (
                <div
                  className="topbar-dropdown"
                  onClick={() => setShowActionsMenu(false)}
                >
                  <button
                    className="topbar-dropdown-item"
                    onClick={() => navigate("/system/status")}
                  >
                    System Status
                  </button>
                  <button
                    className="topbar-dropdown-item"
                    onClick={() => navigate("/settings/general")}
                  >
                    Settings
                  </button>
                  <button
                    className="topbar-dropdown-item"
                    onClick={() => setShowCommandPalette(true)}
                  >
                    🔍 Command Palette (⌘K)
                  </button>
                  <button
                    className="topbar-dropdown-item"
                    onClick={() => setShowShortcutsModal(true)}
                  >
                    ⌨️ Keyboard Shortcuts (?)
                  </button>
                  <div className="topbar-dropdown-separator" />
                  <button
                    className="topbar-dropdown-item"
                    onClick={() => {
                      if (confirm("Restart Seedarr?")) {
                        apiClient
                          .post("/system/restart")
                          .catch((err) =>
                            console.error("System action failed:", err),
                          );
                      }
                    }}
                  >
                    Restart
                  </button>
                  <button
                    className="topbar-dropdown-item topbar-dropdown-danger"
                    onClick={() => {
                      if (confirm("Shut down Seedarr?")) {
                        apiClient
                          .post("/system/shutdown")
                          .catch((err) =>
                            console.error("System action failed:", err),
                          );
                      }
                    }}
                  >
                    Shutdown
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
              <Route path="/torrents/add" element={<AddTorrentPage />} />
              <Route path="/add-torrent" element={<AddTorrentPage />} />
              <Route path="/torrents/history" element={<DownloadHistory />} />
              <Route path="/history" element={<DownloadHistory />} />
              <Route path="/activity/torrents" element={<TorrentIndex />} />
              <Route path="/torrents" element={<DownloadHistory />} />
              <Route path="/torrents/:id" element={<TorrentDetails />} />
              <Route
                path="/activity/torrents/:id"
                element={<TorrentDetails />}
              />
              <Route
                path="/activity/client/:id"
                element={<DownloadClientTorrents />}
              />
              <Route
                path="/torrents/client/:id"
                element={<DownloadClientTorrents />}
              />
              <Route path="/activity" element={<Activity />} />
              <Route path="/activity/metrics" element={<Activity />} />
              <Route path="/downloadplusplus" element={<TrackerBoost />} />
              <Route path="/download++" element={<TrackerBoost />} />
              <Route path="/trackerboost" element={<TrackerBoost />} />
              <Route path="/tracker" element={<TrackerServer />} />
              <Route path="/tracker/inbuilt" element={<TrackerServer />} />
              <Route path="/tracker/trackerboost" element={<TrackerBoost />} />
              <Route path="/tracker/boost" element={<TrackerBoost />} />
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
      />
      <KeyboardShortcutsModal
        isOpen={showShortcutsModal}
        onClose={() => setShowShortcutsModal(false)}
      />
    </div>
  );
}

export default App;
