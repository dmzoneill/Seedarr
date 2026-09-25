import { useState, useEffect, useCallback, useRef } from "react";
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
import SystemResources from "./pages/SystemResources";
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
import SystemTerminal from "./pages/SystemTerminal";
import DatabaseExplorer from "./pages/DatabaseExplorer";
import DeveloperDiagnostics from "./pages/DeveloperDiagnostics";
import ApiDocsPage from "./pages/ApiDocsPage";
import DownloadClientTorrents from "./pages/DownloadClientTorrents";
import { AutomationPage } from "./pages/AutomationPage";
import LoginPage from "./pages/LoginPage";
import StatusBar from "./components/StatusBar";
import ToastContainer from "./components/Toast";
import AriaLiveAnnouncer from "./components/AriaLiveAnnouncer";
import ErrorBoundary from "./components/ErrorBoundary";
import SignalRProvider from "./components/SignalRProvider";
import { useSignalR, stopSignalR } from "./api/signalr";
import { broadcastLogout, subscribeAuthChannel } from "./utils/authChannel";
import { useModalStack } from "./components/ModalProvider";
import AddTorrentModal from "./components/AddTorrentModal";
import CommandPalette from "./components/CommandPalette";
import KeyboardShortcutsModal from "./components/KeyboardShortcutsModal";
import { AiCopilotDrawer } from "./components/AiCopilotDrawer";
import { useIdleTimer } from "./hooks/useIdleTimer";
import { IdleLockModal, IdleCountdownModal } from "./components/IdleLockModal";
import {
  GettingStartedModal,
  STORAGE_KEY_HIDE_GUIDE,
} from "./components/GettingStartedModal";
import AddTorrentPage from "./pages/AddTorrentPage";
import { usePermissions } from "./hooks/usePermissions";
import { useAppStore } from "./store/app";
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
  AutomationIcon,
  CodeIcon,
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
import {
  useTranslation,
  useI18nStore,
  STORAGE_KEY_LANGUAGE,
  isSupportedLocale,
  type LocaleCode,
} from "./i18n";
import LanguageSelector from "./components/LanguageSelector";
import { SETTINGS_GROUPS } from "./pages/settings/settingsNavData";
import {
  trackPageView,
  trackThemeChange,
  setAnalyticsInstanceUuid,
  trackConfigAdoption,
} from "./utils/analytics";

const systemSubItems = [
  { path: "/system/status", label: "Status" },
  { path: "/system/resources", label: "Resources", labelKey: "nav.resources" },
  { path: "/system/tasks", label: "Tasks" },
  { path: "/system/backup", label: "Backup" },
  { path: "/system/updates", label: "Updates" },
  { path: "/system/events", label: "Events" },
  { path: "/system/logfiles", label: "Log Files" },
  { path: "/system/network", label: "Network" },
];

const developerSubItems = [
  { path: "/developer/database", label: "Database", labelKey: "nav.database" },
  { path: "/developer/terminal", label: "Terminal", labelKey: "nav.terminal" },
  { path: "/developer/api", label: "API Reference", labelKey: "nav.apiReference" },
  { path: "/developer/diagnostics", label: "Diagnostics", labelKey: "nav.diagnostics" },
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

  const handleToggleTheme = useCallback(() => {
    const nextTheme = theme === "dark" ? "light" : "dark";
    trackThemeChange(nextTheme);
    toggleTheme();
  }, [theme, toggleTheme]);

  useEffect(() => {
    const pagePath = location.pathname + location.search;
    trackPageView(pagePath);
  }, [location.pathname, location.search]);

  const { connected, isReconnecting, reconnect } = useSignalR();
  const { showToast } = useToast();
  const [currentUser, setCurrentUser] = useState<
    import("./api/types").CurrentUser | null
  >(null);
  const setStoreCurrentUser = useAppStore((s) => s.setCurrentUser);
  const updateCurrentUser = useCallback(
    (user: import("./api/types").CurrentUser | null) => {
      setCurrentUser(user);
      setStoreCurrentUser(user);
    },
    [setStoreCurrentUser],
  );
  const { isAdmin, canAddTorrent } = usePermissions(currentUser);
  const [isManuallyLocked, setIsManuallyLocked] = useState(false);
  const [lockReason, setLockReason] = useState<"idle" | "expired">("idle");
  const [isRestarting, setIsRestarting] = useState(false);

  const startRestartPolling = useCallback(() => {
    setIsRestarting(true);
    setTimeout(() => {
      const pollInterval = setInterval(async () => {
        try {
          const res = await fetch("/api/v1/system/status");
          if (res.ok) {
            clearInterval(pollInterval);
            window.location.reload();
          }
        } catch {
          // Backend is restarting; continue polling
        }
      }, 1500);
    }, 1500);
  }, []);

  const handleRestart = useCallback(async () => {
    if (!confirm(t("topbar.restartConfirm", undefined, "Restart Seedarr?"))) {
      return;
    }

    startRestartPolling();
    try {
      await apiClient.post("/system/restart");
    } catch (err) {
      console.error("System restart trigger failed:", err);
    }
  }, [t, startRestartPolling]);

  useEffect(() => {
    const onRestartEvent = () => {
      startRestartPolling();
    };
    window.addEventListener("seedarr:restarting", onRestartEvent);
    return () => {
      window.removeEventListener("seedarr:restarting", onRestartEvent);
    };
  }, [startRestartPolling]);

  const {
    isIdle,
    isWarning,
    remainingSeconds,
    resetTimer,
    lockSession,
    unlockSession,
  } = useIdleTimer({
    enabled: Boolean(currentUser && location.pathname !== "/login"),
    onIdle: () => {
      setLockReason("idle");
    },
  });

  const isLocked =
    (isIdle || isManuallyLocked) &&
    Boolean(currentUser && location.pathname !== "/login");

  const handleUnlockSession = useCallback(() => {
    setIsManuallyLocked(false);
    unlockSession();
    loadUser();
    reconnect();
    showToast(
      t("auth.sessionUnlocked", undefined, "Session unlocked successfully"),
      "success",
    );
  }, [unlockSession, reconnect, showToast, t]);

  const handleStayLoggedIn = useCallback(async () => {
    resetTimer();
    try {
      await apiClient.refreshSession(1);
    } catch {
      setLockReason("expired");
      setIsManuallyLocked(true);
    }
  }, [resetTimer]);

  const loadUser = async () => {
    try {
      const user = await apiClient.getCurrentUser();
      updateCurrentUser(user);
    } catch {
      // ignore
    }
  };

  useEffect(() => {
    loadUser();
    apiClient
      .getSetupStatus()
      .then((status) => {
        if (typeof window !== "undefined" && window.navigator?.webdriver) {
          setShowGettingStartedModal(false);
          return;
        }
        if (status.isSetupCompleted) {
          setShowGettingStartedModal(false);
          localStorage.setItem(STORAGE_KEY_HIDE_GUIDE, "true");
        } else if (localStorage.getItem(STORAGE_KEY_HIDE_GUIDE) !== "true") {
          setShowGettingStartedModal(true);
        }
      })
      .catch(() => {
        // Keep existing state
      });
  }, []);

  useEffect(() => {
    const unsubscribe = subscribeAuthChannel((event) => {
      if (event.type === "AUTH_LOGOUT") {
        updateCurrentUser(null);
        stopSignalR();
        navigate("/login");
        showToast(
          t(
            "auth.loggedOutOtherTab",
            undefined,
            "You were logged out in another tab.",
          ),
          "info",
        );
      } else if (event.type === "AUTH_SESSION_EXPIRED") {
        if (currentUser && location.pathname !== "/login") {
          setLockReason("expired");
          setIsManuallyLocked(true);
          showToast(
            t(
              "auth.sessionExpired",
              undefined,
              "Your session has expired. Enter your password to resume.",
            ),
            "warning",
          );
        } else {
          updateCurrentUser(null);
          stopSignalR();
          navigate("/login");
        }
      } else if (event.type === "AUTH_LOGIN") {
        if (event.user) {
          updateCurrentUser(event.user);
        } else {
          loadUser();
        }
        reconnect();
        if (location.pathname === "/login") {
          navigate("/");
        }
        showToast(
          t("auth.loggedInOtherTab", undefined, "Signed in from another tab."),
          "info",
        );
      }
    });

    return () => {
      unsubscribe();
    };
  }, [navigate, reconnect, showToast, t, location.pathname]);

  const handleLogout = async () => {
    try {
      await apiClient.logout();
    } catch {
      // ignore
    } finally {
      broadcastLogout();
      updateCurrentUser(null);
      stopSignalR();
      navigate("/login");
    }
  };

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

  const closeOtherModals = useCallback(() => {
    setShowAddTorrentModal(false);
    setShowShortcutsModal(false);
    setShowGettingStartedModal(false);
  }, []);

  const openCommandPalette = useCallback(() => {
    closeOtherModals();
    setShowCommandPalette(true);
  }, [closeOtherModals]);

  const toggleCommandPalette = useCallback(() => {
    setShowCommandPalette((prev) => {
      if (!prev) {
        closeOtherModals();
        return true;
      }
      return false;
    });
  }, [closeOtherModals]);

  const { modalCount } = useModalStack();

  const isAnyModalOpen = useCallback(() => {
    if (
      showAddTorrentModal ||
      showShortcutsModal ||
      showGettingStartedModal ||
      showCommandPalette ||
      isRestarting ||
      isLocked ||
      isWarning
    ) {
      return true;
    }
    if (modalCount > 0) {
      return true;
    }
    if (typeof document !== "undefined") {
      const activeModal = document.querySelector(
        'dialog[open], [role="dialog"], [aria-modal="true"], .modal-overlay, .modal-backdrop',
      );
      if (activeModal) return true;
    }
    return false;
  }, [
    showAddTorrentModal,
    showShortcutsModal,
    showGettingStartedModal,
    showCommandPalette,
    modalCount,
    isRestarting,
    isLocked,
    isWarning,
  ]);

  const isAnyModalOpenRef = useRef(false);
  isAnyModalOpenRef.current = isAnyModalOpen();
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
  const isDeveloperRoute = location.pathname.startsWith("/developer");
  const { data: generalConfig } = useGeneralConfig();

  useEffect(() => {
    if (
      generalConfig?.uiLanguage &&
      isSupportedLocale(generalConfig.uiLanguage)
    ) {
      try {
        const stored = localStorage.getItem(STORAGE_KEY_LANGUAGE);
        if (!stored) {
          useI18nStore
            .getState()
            .setLocale(generalConfig.uiLanguage as LocaleCode);
        }
      } catch {
        useI18nStore
          .getState()
          .setLocale(generalConfig.uiLanguage as LocaleCode);
      }
    }
  }, [generalConfig?.uiLanguage]);

  useEffect(() => {
    if (generalConfig?.instanceUuid) {
      setAnalyticsInstanceUuid(generalConfig.instanceUuid);
      trackConfigAdoption({
        has_auth: generalConfig.authenticationEnabled,
        has_ssl: generalConfig.enableSsl ?? false,
        has_watch_folder: generalConfig.watchFolderEnabled,
        theme: generalConfig.themeStyle,
        color_scheme: generalConfig.colorScheme,
        language: generalConfig.uiLanguage,
      });
    }
  }, [generalConfig]);
  const { data: downloadClients } = useDownloadClients();
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

      const isModalActive =
        isAnyModalOpenRef.current ||
        (typeof document !== "undefined" &&
          Boolean(
            document.querySelector(
              'dialog[open], [role="dialog"], [aria-modal="true"], .modal-overlay, .modal-backdrop',
            ),
          ));

      // Alt+M toggles sidebar collapse (Option+M on Mac sends code 'KeyM')
      if (e.altKey && (e.key.toLowerCase() === "m" || e.code === "KeyM")) {
        if (isModalActive) return;
        e.preventDefault();
        toggleSidebar();
        return;
      }

      // Cmd+K / Ctrl+K - do not intercept if focus is inside SystemTerminal
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        if (activeEl?.closest(".terminal-container")) {
          return;
        }
        e.preventDefault();
        toggleCommandPalette();
        return;
      }

      // If typing inside an input/textarea, do not intercept single-key shortcuts
      if (isInputActive) return;

      // Gate single-key navigation and multi-key sequence when any modal is active
      if (isModalActive) {
        pendingGKey = false;
        if (pendingGTimer) {
          clearTimeout(pendingGTimer);
          pendingGTimer = null;
        }
        return;
      }

      // "/" opens search / command palette
      if (e.key === "/" && !e.ctrlKey && !e.metaKey) {
        e.preventDefault();
        openCommandPalette();
        return;
      }

      // "?" opens Keyboard Shortcuts cheat sheet
      if (e.key === "?" || (e.shiftKey && e.key === "/")) {
        e.preventDefault();
        setShowCommandPalette(false);
        setShowShortcutsModal(true);
        return;
      }

      // "g" sequence navigation (e.g. g then d => dashboard)
      const keyLower = e.key.toLowerCase();
      if (
        keyLower === "g" &&
        !pendingGKey &&
        !e.ctrlKey &&
        !e.metaKey &&
        !e.altKey
      ) {
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

        if (keyLower === "d") {
          e.preventDefault();
          e.stopImmediatePropagation?.();
          navigate("/");
        } else if (keyLower === "t") {
          e.preventDefault();
          e.stopImmediatePropagation?.();
          navigate("/torrents");
        } else if (keyLower === "h") {
          e.preventDefault();
          e.stopImmediatePropagation?.();
          navigate("/activity/history");
        } else if (keyLower === "b") {
          e.preventDefault();
          e.stopImmediatePropagation?.();
          navigate("/tracker/trackerboost");
        } else if (keyLower === "m") {
          e.preventDefault();
          e.stopImmediatePropagation?.();
          navigate("/activity/metrics");
        } else if (keyLower === "p") {
          e.preventDefault();
          e.stopImmediatePropagation?.();
          navigate("/peermap");
        } else if (keyLower === "s") {
          e.preventDefault();
          e.stopImmediatePropagation?.();
          navigate("/settings/general");
        } else if (keyLower === "c") {
          e.preventDefault();
          e.stopImmediatePropagation?.();
          navigate("/system/terminal");
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
      if (pendingGTimer) clearTimeout(pendingGTimer);
    };
  }, [navigate, toggleCommandPalette, openCommandPalette]);

  if (location.pathname === "/login") {
    return (
      <ErrorBoundary>
        <LoginPage
          onLoginSuccess={(returnUrl) => {
            loadUser();
            navigate(returnUrl || "/");
          }}
        />
      </ErrorBoundary>
    );
  }

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
              {canAddTorrent && (
                <NavLink
                  to="/torrents/add"
                  className="sidebar-nav-item sidebar-nav-sub"
                  title={t("nav.addTorrent", undefined, "Add Torrent")}
                >
                  <span style={{ fontSize: "1.1rem", lineHeight: 1 }}>+</span>{" "}
                  <span>{t("nav.addTorrent", undefined, "Add Torrent")}</span>
                </NavLink>
              )}
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
              {downloadClients &&
                downloadClients.filter((c) => c.enable).length > 1 && (
                  <NavLink
                    to="/activity/client/all"
                    className="sidebar-nav-item sidebar-nav-sub"
                    title="All Clients"
                  >
                    <DownloadAgentIcon /> <span>All Clients</span>
                  </NavLink>
                )}
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
            to="/automation"
            className="sidebar-nav-item"
            title={t("nav.automation", undefined, "Automation")}
          >
            <AutomationIcon />{" "}
            <span>{t("nav.automation", undefined, "Automation")}</span>
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
            systemSubItems.map((item) => {
              const labelText = (item as any).labelKey
                ? t((item as any).labelKey, undefined, item.label)
                : item.label;
              return (
                <NavLink
                  key={item.path}
                  to={item.path}
                  className="sidebar-nav-item sidebar-nav-sub"
                  title={labelText}
                >
                  <span>{labelText}</span>
                </NavLink>
              );
            })}

          <NavLink
            to="/developer/database"
            className={`sidebar-nav-item ${isDeveloperRoute ? "active" : ""}`}
            title={t("nav.developer", undefined, "Developer")}
          >
            <CodeIcon /> <span>{t("nav.developer", undefined, "Developer")}</span>
          </NavLink>
          {isDeveloperRoute &&
            developerSubItems.map((item) => {
              const labelText = (item as any).labelKey
                ? t((item as any).labelKey, undefined, item.label)
                : item.label;
              return (
                <NavLink
                  key={item.path}
                  to={item.path}
                  className="sidebar-nav-item sidebar-nav-sub"
                  title={labelText}
                >
                  <span>{labelText}</span>
                </NavLink>
              );
            })}
        </nav>
      </aside>

      <div className="main-wrapper">
        {!connected && (
          <div
            role="alert"
            className="signalr-reconnection-banner"
            style={{
              backgroundColor: isReconnecting
                ? "rgba(245, 158, 11, 0.15)"
                : "rgba(239, 68, 68, 0.15)",
              borderBottom: isReconnecting
                ? "1px solid rgba(245, 158, 11, 0.4)"
                : "1px solid rgba(239, 68, 68, 0.4)",
              color: isReconnecting ? "#fbbf24" : "#fca5a5",
              padding: "0.5rem 1.25rem",
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              fontSize: "0.85rem",
              fontWeight: 500,
              zIndex: 1001,
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.6rem",
              }}
            >
              <span
                className="reconnect-pulse-dot"
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "50%",
                  backgroundColor: isReconnecting ? "#f59e0b" : "#ef4444",
                  display: "inline-block",
                  boxShadow: isReconnecting
                    ? "0 0 8px #f59e0b"
                    : "0 0 8px #ef4444",
                  animation: isReconnecting
                    ? "skeleton-pulse 1.5s infinite ease-in-out"
                    : undefined,
                }}
              />
              <span>
                {isReconnecting
                  ? t(
                      "signalr.reconnecting",
                      undefined,
                      "Real-time connection lost. Attempting to reconnect...",
                    )
                  : t(
                      "signalr.disconnected",
                      undefined,
                      "Disconnected from server. Click Retry Now to reconnect.",
                    )}
              </span>
            </div>
            <button
              type="button"
              onClick={() => reconnect()}
              className="btn btn-small"
              style={{
                backgroundColor: isReconnecting ? "#f59e0b" : "#ef4444",
                color: isReconnecting ? "#000" : "#fff",
                fontWeight: 600,
                border: "none",
                padding: "0.2rem 0.65rem",
                cursor: "pointer",
                borderRadius: "4px",
                fontSize: "0.75rem",
              }}
            >
              {t("signalr.retryNow", undefined, "Retry Now")}
            </button>
          </div>
        )}
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
                border: "1px solid var(--border-light)",
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
              onClick={openCommandPalette}
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
                onClick={openCommandPalette}
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
                  border: "1px solid var(--border)",
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
              type="button"
              className="topbar-btn"
              onClick={() => setShowShortcutsModal(true)}
              title={t(
                "topbar.keyboardShortcuts",
                undefined,
                "Keyboard Shortcuts (?)",
              )}
              aria-label={t(
                "topbar.keyboardShortcuts",
                undefined,
                "Keyboard Shortcuts (?)",
              )}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.35rem",
                padding: "0.25rem 0.5rem",
                borderRadius: "4px",
                cursor: "pointer",
                fontSize: "0.85rem",
              }}
            >
              <span style={{ fontSize: "1rem", lineHeight: 1 }}>⌨️</span>
              <kbd
                style={{
                  backgroundColor: "rgba(255, 255, 255, 0.08)",
                  border: "1px solid var(--border)",
                  borderRadius: "3px",
                  padding: "0.05rem 0.35rem",
                  fontSize: "0.7rem",
                  color: "var(--text-muted)",
                  fontFamily: "monospace",
                  lineHeight: 1.2,
                }}
              >
                ?
              </kbd>
            </button>
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
              aria-label={t(
                "nav.gettingStarted",
                undefined,
                "Getting Started Guide",
              )}
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
                    ? unmaskedApiKey ||
                      (generalConfig.apiKey.includes("*")
                        ? "••••••••••••••••••••••••••••••••"
                        : generalConfig.apiKey)
                    : "••••••••••••••••••••••••••••••••"}
                </span>
              </button>
            )}
            <button
              className="topbar-btn"
              onClick={handleToggleTheme}
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
              aria-label={t(
                "topbar.supportSeedarr",
                undefined,
                "Support Seedarr",
              )}
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
                    border: "1px solid var(--border)",
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
                    onClick={openCommandPalette}
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
                  <button
                    className="topbar-dropdown-item"
                    role="menuitem"
                    onClick={() => {
                      setShowActionsMenu(false);
                      setLockReason("idle");
                      setIsManuallyLocked(true);
                    }}
                  >
                    {t("auth.lockScreen", undefined, "🔒 Lock Screen")}
                  </button>
                  <button
                    className="topbar-dropdown-item"
                    role="menuitem"
                    onClick={handleLogout}
                  >
                    {t("topbar.logout", undefined, "Log Out")}
                  </button>
                  {isAdmin && (
                    <>
                      <div className="topbar-dropdown-separator" />
                      <button
                        className="topbar-dropdown-item"
                        role="menuitem"
                        onClick={handleRestart}
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
                            apiClient.post("/system/shutdown").catch((err) => {
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
                    </>
                  )}
                </div>
              )}
            </div>
          </div>
        </header>

        <ToastContainer />
        <main className="app-main">
          <ErrorBoundary>
            <Routes>
              <Route
                path="/login"
                element={
                  <LoginPage
                    onLoginSuccess={(returnUrl) => {
                      loadUser();
                      navigate(returnUrl || "/");
                    }}
                  />
                }
              />
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
              <Route path="/automation" element={<AutomationPage />} />
              <Route path="/settings/tags" element={<Tags />} />
              <Route path="/settings/:section?" element={<Settings />} />
              <Route path="/system/status" element={<SystemStatus />} />
              <Route path="/system/resources" element={<SystemResources />} />
              <Route
                path="/system/telemetry"
                element={<Navigate to="/system/resources" replace />}
              />
              <Route path="/system/tasks" element={<SystemTasks />} />
              <Route path="/system/logs" element={<SystemLogs />} />
              <Route path="/system/backup" element={<SystemBackup />} />
              <Route path="/system/updates" element={<SystemUpdates />} />
              <Route path="/system/events" element={<SystemEvents />} />
              <Route path="/system/logfiles" element={<SystemLogFiles />} />
              <Route path="/system/network" element={<SystemNetwork />} />
              {/* Developer Tools */}
              <Route
                path="/developer"
                element={<Navigate to="/developer/database" replace />}
              />
              <Route path="/developer/database" element={<DatabaseExplorer />} />
              <Route path="/developer/terminal" element={<SystemTerminal />} />
              <Route path="/developer/api" element={<ApiDocsPage />} />
              <Route path="/developer/diagnostics" element={<DeveloperDiagnostics />} />

              {/* Legacy Navigation Redirects */}
              <Route
                path="/terminal"
                element={<Navigate to="/developer/terminal" replace />}
              />
              <Route
                path="/system/terminal"
                element={<Navigate to="/developer/terminal" replace />}
              />
              <Route
                path="/system/database"
                element={<Navigate to="/developer/database" replace />}
              />
              <Route
                path="/system/api"
                element={<Navigate to="/developer/api" replace />}
              />
              <Route
                path="/system/swagger"
                element={<Navigate to="/developer/api" replace />}
              />
              <Route
                path="/api-docs"
                element={<Navigate to="/developer/api" replace />}
              />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </ErrorBoundary>
        </main>
        <StatusBar connected={connected} isReconnecting={isReconnecting} />
      </div>
      <AriaLiveAnnouncer />
      <SignalRProvider />
      <AiCopilotDrawer />
      {showAddTorrentModal && (
        <AddTorrentModal onClose={() => setShowAddTorrentModal(false)} />
      )}
      <CommandPalette
        isOpen={showCommandPalette}
        onClose={() => setShowCommandPalette(false)}
        onOpenShortcuts={() => {
          setShowCommandPalette(false);
          setShowShortcutsModal(true);
        }}
        onOpenAddTorrent={() => {
          setShowCommandPalette(false);
          setShowAddTorrentModal(true);
        }}
        onOpenGettingStarted={() => {
          setShowCommandPalette(false);
          setShowGettingStartedModal(true);
        }}
      />
      <KeyboardShortcutsModal
        isOpen={showShortcutsModal}
        onClose={() => setShowShortcutsModal(false)}
      />
      <GettingStartedModal
        isOpen={showGettingStartedModal}
        onClose={() => setShowGettingStartedModal(false)}
      />
      <IdleLockModal
        isOpen={isLocked}
        currentUser={currentUser}
        lockReason={lockReason}
        onUnlock={handleUnlockSession}
        onLogout={handleLogout}
      />
      <IdleCountdownModal
        isOpen={
          isWarning &&
          !isLocked &&
          Boolean(currentUser && location.pathname !== "/login")
        }
        remainingSeconds={remainingSeconds}
        onStayLoggedIn={handleStayLoggedIn}
        onLockNow={() => {
          setLockReason("idle");
          lockSession();
        }}
        onLogout={handleLogout}
      />
      {isRestarting && (
        <div
          className="modal-overlay"
          role="dialog"
          aria-modal="true"
          style={{
            zIndex: 99999,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            backgroundColor: "rgba(0, 0, 0, 0.75)",
            backdropFilter: "blur(4px)",
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <div
            className="modal"
            style={{
              maxWidth: 420,
              width: "90%",
              textAlign: "center",
              padding: "2.5rem 2rem",
              borderRadius: "12px",
              boxShadow: "0 8px 32px rgba(0, 0, 0, 0.5)",
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
            }}
          >
            <div style={{ marginBottom: "1.25rem" }}>
              <SeedarrLogo size={48} />
            </div>
            <h2
              style={{
                margin: "0 0 0.5rem",
                fontSize: "1.35rem",
                fontWeight: 600,
                color: "var(--text-primary)",
              }}
            >
              Seedarr is restarting...
            </h2>
            <p
              style={{
                margin: "0 0 1.5rem",
                fontSize: "0.875rem",
                color: "var(--text-secondary)",
                lineHeight: 1.5,
              }}
            >
              Please wait while the system restarts and re-establishes
              connection.
            </p>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
                color: "var(--accent, #ffd166)",
                fontSize: "0.9rem",
                fontWeight: 500,
              }}
            >
              <span
                style={{
                  display: "inline-block",
                  width: "18px",
                  height: "18px",
                  border: "2px solid rgba(255, 255, 255, 0.2)",
                  borderTopColor: "currentColor",
                  borderRadius: "50%",
                  animation: "spin 1s linear infinite",
                }}
              />
              <span>Reconnecting to server...</span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default App;
