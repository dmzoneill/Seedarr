import { Routes, Route, Link } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import SystemStatus from './pages/SystemStatus';

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
            <Link to="/system/status">System</Link>
          </nav>
        </div>
      </header>
      <main className="app-main">
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/system/status" element={<SystemStatus />} />
        </Routes>
      </main>
      <footer className="app-footer">
        <span>Seedarr</span>
      </footer>
    </div>
  );
}

export default App;
