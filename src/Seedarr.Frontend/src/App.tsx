import { Routes, Route, Link, NavLink, useLocation } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import TorrentIndex from './pages/TorrentIndex';
import TorrentDetails from './pages/TorrentDetails';
import Settings from './pages/Settings';
import SystemStatus from './pages/SystemStatus';
import SystemTasks from './pages/SystemTasks';
import StatusBar from './components/StatusBar';
import ToastContainer from './components/Toast';
import ErrorBoundary from './components/ErrorBoundary';
import SignalRProvider from './components/SignalRProvider';
import SeedarrLogo from './components/icons/SeedarrLogo';
import { DashboardIcon, TorrentIcon, SettingsIcon, SystemIcon } from './components/icons/NavIcons';
import { useTheme } from './context/ThemeContext';

function SunIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
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
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
    </svg>
  );
}

function App() {
  const location = useLocation();
  const isSystemRoute = location.pathname.startsWith('/system');
  const { theme, toggleTheme } = useTheme();

  return (
    <div className="app">
      <header className="app-header">
        <div className="app-header-content">
          <Link to="/" className="app-logo">
            <SeedarrLogo size={28} />
            Seedarr
          </Link>
          <nav className="app-nav">
            <NavLink to="/" end><DashboardIcon /> Dashboard</NavLink>
            <NavLink to="/torrents"><TorrentIcon /> Torrents</NavLink>
            <NavLink to="/settings"><SettingsIcon /> Settings</NavLink>
            <NavLink to="/system/status"><SystemIcon /> System</NavLink>
          </nav>
          <button
            className="theme-toggle"
            onClick={toggleTheme}
            title={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
          >
            {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
          </button>
        </div>
        {isSystemRoute && (
          <nav className="app-subnav">
            <Link to="/system/status">Status</Link>
            <Link to="/system/tasks">Tasks</Link>
          </nav>
        )}
      </header>
      <ToastContainer />
      <main className="app-main">
        <ErrorBoundary>
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/torrents" element={<TorrentIndex />} />
            <Route path="/torrents/:id" element={<TorrentDetails />} />
            <Route path="/settings" element={<Settings />} />
            <Route path="/system/status" element={<SystemStatus />} />
            <Route path="/system/tasks" element={<SystemTasks />} />
          </Routes>
        </ErrorBoundary>
      </main>
      <StatusBar />
      <SignalRProvider />
    </div>
  );
}

export default App;
