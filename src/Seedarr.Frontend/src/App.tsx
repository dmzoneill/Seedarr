import { Routes, Route, Link } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import TorrentIndex from './pages/TorrentIndex';
import TorrentDetails from './pages/TorrentDetails';
import Settings from './pages/Settings';
import SystemStatus from './pages/SystemStatus';
import StatusBar from './components/StatusBar';

function App() {
  return (
    <div className="app">
      <header className="app-header">
        <div className="app-header-content">
          <Link to="/" className="app-logo">
            Seedarr
          </Link>
          <nav className="app-nav">
            <Link to="/">Dashboard</Link>
            <Link to="/torrents">Torrents</Link>
            <Link to="/settings">Settings</Link>
            <Link to="/system/status">System</Link>
          </nav>
        </div>
      </header>
      <main className="app-main">
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/torrents" element={<TorrentIndex />} />
          <Route path="/torrents/:id" element={<TorrentDetails />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/system/status" element={<SystemStatus />} />
        </Routes>
      </main>
      <StatusBar />
    </div>
  );
}

export default App;
