import { Routes, Route, Link, useLocation } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import TorrentIndex from './pages/TorrentIndex';
import TorrentDetails from './pages/TorrentDetails';
import Settings from './pages/Settings';
import SystemStatus from './pages/SystemStatus';
import SystemTasks from './pages/SystemTasks';
import StatusBar from './components/StatusBar';
import SeedarrLogo from './components/icons/SeedarrLogo';
import { DashboardIcon, TorrentIcon, SettingsIcon, SystemIcon } from './components/icons/NavIcons';

function App() {
  const location = useLocation();
  const isSystemRoute = location.pathname.startsWith('/system');

  return (
    <div className="app">
      <header className="app-header">
        <div className="app-header-content">
          <Link to="/" className="app-logo">
            <SeedarrLogo size={28} />
            Seedarr
          </Link>
          <nav className="app-nav">
            <Link to="/"><DashboardIcon /> Dashboard</Link>
            <Link to="/torrents"><TorrentIcon /> Torrents</Link>
            <Link to="/settings"><SettingsIcon /> Settings</Link>
            <Link to="/system/status"><SystemIcon /> System</Link>
          </nav>
        </div>
        {isSystemRoute && (
          <nav className="app-subnav">
            <Link to="/system/status">Status</Link>
            <Link to="/system/tasks">Tasks</Link>
          </nav>
        )}
      </header>
      <main className="app-main">
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/torrents" element={<TorrentIndex />} />
          <Route path="/torrents/:id" element={<TorrentDetails />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/system/status" element={<SystemStatus />} />
          <Route path="/system/tasks" element={<SystemTasks />} />
        </Routes>
      </main>
      <StatusBar />
    </div>
  );
}

export default App;
