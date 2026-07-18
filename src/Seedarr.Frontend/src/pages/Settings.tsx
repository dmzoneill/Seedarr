import { useParams } from 'react-router';
import { GeneralTab } from './settings/GeneralTab';
import { SeedingTab } from './settings/SeedingTab';
import { BitTorrentTab } from './settings/BitTorrentTab';
import { NetworkTab } from './settings/NetworkTab';
import { PeerProtocolTab } from './settings/PeerProtocolTab';
import { ProtocolsTab } from './settings/ProtocolsTab';
import { SimulationTab } from './settings/SimulationTab';
import { TrackerServerTab } from './settings/TrackerServerTab';
import { SchedulerTab } from './settings/SchedulerTab';
import { AdvancedTab } from './settings/AdvancedTab';
import { IndexersTab } from './settings/IndexersTab';
import { ConnectionsTab } from './settings/ConnectionsTab';
import { DownloadClientsTab } from './settings/DownloadClientsTab';
import { NotificationsTab } from './settings/NotificationsTab';
import { WebUITab } from './settings/WebUITab';

const sectionTitles: Record<string, string> = {
  general: 'General',
  webui: 'Web UI',
  notifications: 'Notifications',
  seeding: 'Seeding',
  bittorrent: 'BitTorrent',
  network: 'Network',
  'peer-protocol': 'Peer Protocol',
  protocols: 'Protocols',
  simulation: 'Simulation',
  'tracker-server': 'Tracker Server',
  scheduler: 'Scheduler',
  indexers: 'Indexers',
  connections: 'Connections',
  'download-clients': 'Download Clients',
  advanced: 'Advanced',
};

function Settings() {
  const { section } = useParams<{ section?: string }>();
  const activeSection = section || 'general';
  const title = sectionTitles[activeSection] || 'Settings';

  return (
    <div>
      <h1 className="page-heading">{title}</h1>

      {activeSection === 'general' && <GeneralTab />}
      {activeSection === 'webui' && <WebUITab />}
      {activeSection === 'notifications' && <NotificationsTab />}
      {activeSection === 'seeding' && <SeedingTab />}
      {activeSection === 'bittorrent' && <BitTorrentTab />}
      {activeSection === 'network' && <NetworkTab />}
      {activeSection === 'peer-protocol' && <PeerProtocolTab />}
      {activeSection === 'protocols' && <ProtocolsTab />}
      {activeSection === 'simulation' && <SimulationTab />}
      {activeSection === 'tracker-server' && <TrackerServerTab />}
      {activeSection === 'scheduler' && <SchedulerTab />}
      {activeSection === 'indexers' && <IndexersTab />}
      {activeSection === 'connections' && <ConnectionsTab />}
      {activeSection === 'download-clients' && <DownloadClientsTab />}
      {activeSection === 'advanced' && <AdvancedTab />}
    </div>
  );
}

export default Settings;
