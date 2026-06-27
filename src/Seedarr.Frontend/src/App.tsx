import { useState } from 'react';
import { Routes, Route, NavLink, useLocation, useNavigate } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import TorrentIndex from './pages/TorrentIndex';
import TorrentDetails from './pages/TorrentDetails';
import Activity from './pages/Activity';
import TrackerServer from './pages/TrackerServer';
import Settings from './pages/Settings';
import SystemStatus from './pages/SystemStatus';
import SystemTasks from './pages/SystemTasks';
import SystemLogs from './pages/SystemLogs';
import SystemBackup from './pages/SystemBackup';
import SystemUpdates from './pages/SystemUpdates';
import SystemEvents from './pages/SystemEvents';
import SystemLogFiles from './pages/SystemLogFiles';
import PeerMap from './pages/PeerMap';
import StatusBar from './components/StatusBar';
import ToastContainer from './components/Toast';
import ErrorBoundary from './components/ErrorBoundary';
import SignalRProvider from './components/SignalRProvider';
import SeedarrLogo from './components/icons/SeedarrLogo';
import SeedarrText from './components/icons/SeedarrText';
import { DashboardIcon, TorrentIcon, SettingsIcon, SystemIcon } from './components/icons/NavIcons';
import { useTheme } from './context/ThemeContext';

function ActivityIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="22 12 18 12 15 21 9 3 6 12 2 12" />
    </svg>
  );
}

function TrackerIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="3" />
      <circle cx="12" cy="12" r="9" />
      <line x1="12" y1="3" x2="12" y2="6" />
      <line x1="12" y1="18" x2="12" y2="21" />
      <line x1="3" y1="12" x2="6" y2="12" />
      <line x1="18" y1="12" x2="21" y2="12" />
    </svg>
  );
}

function SunIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="5" />
      <line x1="12" y1="1" x2="12" y2="3" />
      <line x1="12" y1="21" x2="12" y2="23" />
      <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
      <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
      <line x1="1" y1="12" x2="3" y2="12" />
      <line x1="21" y1="12" x2="23" y2="12" />
      <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
      <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
    </svg>
  );
}

function MoonIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
    </svg>
  );
}

function HeartIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" stroke="none">
      <path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" />
    </svg>
  );
}

function UserIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
      <circle cx="12" cy="7" r="4" />
    </svg>
  );
}

function PeerMapIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="5" r="3" />
      <circle cx="5" cy="19" r="3" />
      <circle cx="19" cy="19" r="3" />
      <line x1="12" y1="8" x2="5" y2="16" />
      <line x1="12" y1="8" x2="19" y2="16" />
    </svg>
  );
}

function SearchIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="11" cy="11" r="8" />
      <line x1="21" y1="21" x2="16.65" y2="16.65" />
    </svg>
  );
}

const systemSubItems = [
  { path: '/system/status', label: 'Status' },
  { path: '/system/tasks', label: 'Tasks' },
  { path: '/system/backup', label: 'Backup' },
  { path: '/system/updates', label: 'Updates' },
  { path: '/system/events', label: 'Events' },
  { path: '/system/logfiles', label: 'Log Files' },
];

const settingsSubItems = [
  { path: '/settings/general', label: 'General' },
  { path: '/settings/webui', label: 'Web UI' },
  { path: '/settings/notifications', label: 'Notifications' },
  { path: '/settings/seeding', label: 'Seeding' },
  { path: '/settings/bittorrent', label: 'BitTorrent' },
  { path: '/settings/network', label: 'Network' },
  { path: '/settings/peer-protocol', label: 'Peer Protocol' },
  { path: '/settings/protocols', label: 'Protocols' },
  { path: '/settings/simulation', label: 'Simulation' },
  { path: '/settings/tracker-server', label: 'Tracker Server' },
  { path: '/settings/scheduler', label: 'Scheduler' },
  { path: '/settings/indexers', label: 'Indexers' },
  { path: '/settings/connections', label: 'Connections' },
  { path: '/settings/download-clients', label: 'Download Clients' },
  { path: '/settings/advanced', label: 'Advanced' },
];

function App() {
  const location = useLocation();
  const navigate = useNavigate();
  const { theme, toggleTheme } = useTheme();
  const [searchTerm, setSearchTerm] = useState('');
  const isSettingsRoute = location.pathname.startsWith('/settings');
  const isSystemRoute = location.pathname.startsWith('/system');

  return (
    <div className="app">
      <aside className="sidebar">
        <a href="https://www.seedarr.net" target="_blank" rel="noopener noreferrer" className="sidebar-logo">
          <SeedarrLogo size={96} />
          <SeedarrText width={140} />
        </a>
        <nav className="sidebar-nav">
          <NavLink to="/" end className="sidebar-nav-item">
            <DashboardIcon /> <span>Torrents</span>
          </NavLink>
          <NavLink to="/torrents" className="sidebar-nav-item sidebar-nav-sub">
            <TorrentIcon /> <span>Library</span>
          </NavLink>
          <NavLink to="/activity" className="sidebar-nav-item">
            <ActivityIcon /> <span>Activity</span>
          </NavLink>
          <NavLink to="/tracker" className="sidebar-nav-item">
            <TrackerIcon /> <span>Tracker</span>
          </NavLink>
          <NavLink to="/peermap" className="sidebar-nav-item">
            <PeerMapIcon /> <span>Peer Map</span>
          </NavLink>
          <NavLink
            to="/settings/general"
            className={`sidebar-nav-item ${isSettingsRoute ? 'active' : ''}`}
          >
            <SettingsIcon /> <span>Settings</span>
          </NavLink>
          {isSettingsRoute && settingsSubItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              className="sidebar-nav-item sidebar-nav-sub"
            >
              <span>{item.label}</span>
            </NavLink>
          ))}
          <NavLink to="/system/status" className={`sidebar-nav-item ${isSystemRoute ? 'active' : ''}`}>
            <SystemIcon /> <span>System</span>
          </NavLink>
          {isSystemRoute && systemSubItems.map((item) => (
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
          <div className="topbar-search">
            <SearchIcon />
            <input
              type="text"
              placeholder="Search"
              className="topbar-search-input"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && searchTerm.trim()) {
                  navigate(`/torrents?q=${encodeURIComponent(searchTerm.trim())}`);
                  setSearchTerm('');
                }
              }}
            />
          </div>
          <div className="topbar-actions">
            <button className="topbar-btn" onClick={toggleTheme} title={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}>
              {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
            </button>
            <span className="topbar-btn topbar-heart"><HeartIcon /></span>
            <span className="topbar-btn"><UserIcon /></span>
          </div>
        </header>

        <ToastContainer />
        <main className="app-main">
          <ErrorBoundary>
            <Routes>
              <Route path="/" element={<Dashboard />} />
              <Route path="/torrents" element={<TorrentIndex />} />
              <Route path="/torrents/:id" element={<TorrentDetails />} />
              <Route path="/activity" element={<Activity />} />
              <Route path="/tracker" element={<TrackerServer />} />
              <Route path="/peermap" element={<PeerMap />} />
              <Route path="/settings/:section?" element={<Settings />} />
              <Route path="/system/status" element={<SystemStatus />} />
              <Route path="/system/tasks" element={<SystemTasks />} />
              <Route path="/system/logs" element={<SystemLogs />} />
              <Route path="/system/backup" element={<SystemBackup />} />
              <Route path="/system/updates" element={<SystemUpdates />} />
              <Route path="/system/events" element={<SystemEvents />} />
              <Route path="/system/logfiles" element={<SystemLogFiles />} />
            </Routes>
          </ErrorBoundary>
        </main>
        <StatusBar />
      </div>
      <SignalRProvider />
    </div>
  );
}

export default App;
