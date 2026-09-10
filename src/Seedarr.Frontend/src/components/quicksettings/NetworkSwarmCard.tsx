import { useState, useEffect } from "react";
import {
  useNetworkConfig,
  useSaveNetworkConfig,
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useNetworkStatus,
} from "../../api/hooks";

export function NetworkSwarmCard() {
  const { data: networkConfig, isLoading: isNetworkLoading } =
    useNetworkConfig();
  const saveNetworkConfig = useSaveNetworkConfig();

  const { data: bitTorrentConfig, isLoading: isBtLoading } =
    useBitTorrentConfig();
  const saveBitTorrentConfig = useSaveBitTorrentConfig();

  const { data: networkStatus } = useNetworkStatus();

  const [vpnKillSwitch, setVpnKillSwitch] = useState<boolean>(() => {
    const saved = localStorage.getItem("seedarr_vpn_killswitch");
    return saved !== null ? saved === "true" : true;
  });

  useEffect(() => {
    localStorage.setItem("seedarr_vpn_killswitch", String(vpnKillSwitch));
  }, [vpnKillSwitch]);

  const handleGlobalConnChange = (val: number) => {
    if (!networkConfig) return;
    saveNetworkConfig.mutate({
      ...networkConfig,
      maxGlobalConnections: val,
    });
  };

  const handlePerTorrentConnChange = (val: number) => {
    if (!networkConfig) return;
    saveNetworkConfig.mutate({
      ...networkConfig,
      maxPerTorrentConnections: val,
    });
  };

  const handleToggleBtFlag = (
    flag: "enableDht" | "enablePex" | "enableLpd",
  ) => {
    if (!bitTorrentConfig) return;
    saveBitTorrentConfig.mutate({
      ...bitTorrentConfig,
      [flag]: !bitTorrentConfig[flag],
    });
  };

  if (isNetworkLoading || isBtLoading) {
    return (
      <div className="quick-settings-card">
        <div className="quick-settings-card-header">
          <span className="quick-settings-card-title">Network & Swarms</span>
        </div>
        <div className="quick-settings-loading">
          Loading network settings...
        </div>
      </div>
    );
  }

  const globalConns = networkConfig?.maxGlobalConnections ?? 200;
  const perTorrentConns = networkConfig?.maxPerTorrentConnections ?? 50;
  const dht = bitTorrentConfig?.enableDht ?? true;
  const pex = bitTorrentConfig?.enablePex ?? true;
  const lpd = bitTorrentConfig?.enableLpd ?? true;

  return (
    <div className="quick-settings-card">
      <div className="quick-settings-card-header">
        <div className="quick-settings-card-title-group">
          <span className="quick-settings-card-icon">🌐</span>
          <span className="quick-settings-card-title">Network & Swarms</span>
        </div>
        <div
          className={`quick-settings-vpn-badge ${
            vpnKillSwitch ? "vpn-active" : "vpn-inactive"
          }`}
          title={
            vpnKillSwitch
              ? "Kill Switch Enabled: Non-VPN traffic is blocked"
              : "Kill Switch Disabled: Direct connections permitted"
          }
          onClick={() => setVpnKillSwitch(!vpnKillSwitch)}
          style={{ cursor: "pointer" }}
        >
          {vpnKillSwitch ? "🛡️ Kill Switch ON" : "⚠️ Direct Route"}
        </div>
      </div>

      <div className="quick-settings-card-body">
        {/* Connection Limits Dropdowns */}
        <div className="quick-settings-grid-row">
          <div className="quick-settings-select-group">
            <span className="quick-settings-label">Global Conns</span>
            <select
              className="quick-settings-select"
              value={globalConns}
              onChange={(e) => handleGlobalConnChange(Number(e.target.value))}
            >
              <option value={50}>50</option>
              <option value={100}>100</option>
              <option value={200}>200</option>
              <option value={500}>500</option>
              <option value={1000}>1000</option>
              <option value={2000}>2000</option>
            </select>
          </div>

          <div className="quick-settings-select-group">
            <span className="quick-settings-label">Per-Torrent</span>
            <select
              className="quick-settings-select"
              value={perTorrentConns}
              onChange={(e) =>
                handlePerTorrentConnChange(Number(e.target.value))
              }
            >
              <option value={20}>20</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
              <option value={200}>200</option>
              <option value={500}>500</option>
            </select>
          </div>
        </div>

        {/* Swarm Protocol Chips (DHT, PEX, LPD) */}
        <div className="quick-settings-protocols-section">
          <span className="quick-settings-sublabel">
            Swarm Discovery Protocols:
          </span>
          <div className="quick-settings-protocol-chips">
            <button
              type="button"
              className={`quick-settings-protocol-chip ${dht ? "active" : ""}`}
              onClick={() => handleToggleBtFlag("enableDht")}
              title="Toggle Mainline Distributed Hash Table"
            >
              <span className="quick-settings-chip-dot" />
              DHT {dht ? "ON" : "OFF"}
            </button>
            <button
              type="button"
              className={`quick-settings-protocol-chip ${pex ? "active" : ""}`}
              onClick={() => handleToggleBtFlag("enablePex")}
              title="Toggle Peer Exchange protocol (BEP 11)"
            >
              <span className="quick-settings-chip-dot" />
              PEX {pex ? "ON" : "OFF"}
            </button>
            <button
              type="button"
              className={`quick-settings-protocol-chip ${lpd ? "active" : ""}`}
              onClick={() => handleToggleBtFlag("enableLpd")}
              title="Toggle Local Peer Discovery (LSD/LPD)"
            >
              <span className="quick-settings-chip-dot" />
              LPD {lpd ? "ON" : "OFF"}
            </button>
          </div>
        </div>

        {/* Status info */}
        <div className="quick-settings-network-info">
          <span>
            IP:{" "}
            {networkStatus?.externalIp || networkStatus?.localIp || "127.0.0.1"}
          </span>
          <span>
            UPnP: {networkConfig?.upnpEnabled ? "Active" : "Disabled"}
          </span>
        </div>
      </div>
    </div>
  );
}

export default NetworkSwarmCard;
