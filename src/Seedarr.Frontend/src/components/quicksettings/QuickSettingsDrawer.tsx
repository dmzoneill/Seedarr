import { Link } from "react-router";
import { BandwidthCard } from "./BandwidthCard";
import { QueueConcurrencyCard } from "./QueueConcurrencyCard";
import { NetworkSwarmCard } from "./NetworkSwarmCard";
import { SeedingAutomationCard } from "./SeedingAutomationCard";
import { SettingsIcon } from "../icons/NavIcons";

interface QuickSettingsDrawerProps {
  isOpen: boolean;
  onClose: () => void;
}

export function QuickSettingsDrawer({
  isOpen,
  onClose,
}: QuickSettingsDrawerProps) {
  if (!isOpen) return null;

  return (
    <div className="quick-settings-drawer-container">
      <div className="quick-settings-drawer-header">
        <div className="quick-settings-drawer-title-area">
          <div className="quick-settings-drawer-heading">
            <span className="quick-settings-drawer-badge">⚡ Quick Controls</span>
            <span className="quick-settings-hotkey-badge">Hotkey: Q</span>
          </div>
          <p className="quick-settings-drawer-subtitle">
            Live transfer limits, queue concurrency, protocol swarms & seeding automation
          </p>
        </div>
        <div className="quick-settings-drawer-actions">
          <Link
            to="/settings"
            className="btn btn-small btn-outline quick-settings-full-btn"
            title="Navigate to full configuration tabs"
          >
            <SettingsIcon size={13} />
            <span>Full Settings</span>
          </Link>
          <button
            type="button"
            className="btn btn-small quick-settings-close-btn"
            onClick={onClose}
            title="Close Quick Controls (Q)"
            aria-label="Close Quick Controls"
          >
            ✕
          </button>
        </div>
      </div>

      <div className="quick-settings-grid">
        <BandwidthCard />
        <QueueConcurrencyCard />
        <NetworkSwarmCard />
        <SeedingAutomationCard />
      </div>
    </div>
  );
}

export default QuickSettingsDrawer;
