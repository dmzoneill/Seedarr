import { useState, useEffect } from "react";
import {
  useNetworkConfig,
  useSaveNetworkConfig,
  useNetworkStatus,
  useNetworkInterfaces,
} from "../../api/hooks";
import { SaveBar, SectionCard, NumberInput, TextInput, SelectInput, Toggle } from "./shared";

export function NetworkSettingsTab() {
  const { data: config, isLoading } = useNetworkConfig();
  const saveMutation = useSaveNetworkConfig();
  const { data: netStatus } = useNetworkStatus();
  const { data: interfaces, isLoading: loadingInterfaces } =
    useNetworkInterfaces();
  const [customInterfaceMode, setCustomInterfaceMode] = useState(false);

  const [form, setForm] = useState({
    listeningPort: 51413,
    upnpEnabled: true,
    enableIPv6: true,
    bindInterface: "",
    enableVpnKillSwitch: false,
    maxGlobalConnections: 300,
    maxPerTorrentConnections: 50,
    maxUploadSlots: 8,
    maxConnectionsPerIp: 5,
    maximumHalfOpenConnections: 50,
    peerDscp: 4,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        listeningPort: config.listeningPort ?? 51413,
        upnpEnabled: config.upnpEnabled ?? true,
        enableIPv6: config.enableIPv6 ?? true,
        bindInterface: config.bindInterface || "",
        enableVpnKillSwitch: config.enableVpnKillSwitch ?? false,
        maxGlobalConnections: config.maxGlobalConnections ?? 300,
        maxPerTorrentConnections: config.maxPerTorrentConnections ?? 50,
        maxUploadSlots: config.maxUploadSlots ?? 8,
        maxConnectionsPerIp: config.maxConnectionsPerIp ?? 5,
        maximumHalfOpenConnections: config.maximumHalfOpenConnections ?? 50,
        peerDscp: config.peerDscp ?? 4,
      });
      setDirty(false);
    }
  }, [config]);

  useEffect(() => {
    if (config?.bindInterface && interfaces && interfaces.length > 0) {
      const isKnown = interfaces.some((i) => i.name === config.bindInterface);
      if (!isKnown) {
        setCustomInterfaceMode(true);
      }
    }
  }, [config?.bindInterface, interfaces]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        listeningPort: form.listeningPort,
        upnpEnabled: form.upnpEnabled,
        enableIPv6: form.enableIPv6,
        bindInterface: form.bindInterface,
        enableVpnKillSwitch: form.enableVpnKillSwitch,
        maxGlobalConnections: form.maxGlobalConnections,
        maxPerTorrentConnections: form.maxPerTorrentConnections,
        maxUploadSlots: form.maxUploadSlots,
        maxConnectionsPerIp: form.maxConnectionsPerIp,
        maximumHalfOpenConnections: form.maximumHalfOpenConnections,
        peerDscp: form.peerDscp,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading network configuration...
      </div>
    );
  }

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={saveMutation.isPending}
        isError={saveMutation.isError}
        isSuccess={saveMutation.isSuccess}
        error={saveMutation.error as Error | null}
        onSave={handleSave}
      />

      {netStatus && (
        <div
          className="card"
          style={{
            padding: "1rem",
            borderRadius: "8px",
            border: "1px solid var(--border)",
            marginBottom: "1.25rem",
            backgroundColor: "var(--bg-secondary)",
          }}
        >
          <div
            style={{
              fontSize: "0.85rem",
              fontWeight: 600,
              color: "var(--text-secondary)",
              marginBottom: "0.5rem",
            }}
          >
            Live Network &amp; Interface Status
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: "0.75rem",
              fontSize: "0.82rem",
            }}
          >
            <div>
              Local IP: <strong>{netStatus.localIp || "0.0.0.0"}</strong>
            </div>
            <div>
              Public IP: <strong>{netStatus.externalIp || "Not Detected"}</strong>
            </div>
            <div>
              UPnP Active: <strong>{netStatus.upnpAvailable ? "Yes" : "No"}</strong>
            </div>
            <div>
              Active Port Mappings: <strong>{netStatus.portMappings?.length ?? 0}</strong>
            </div>
          </div>
        </div>
      )}

      <SectionCard
        title="Incoming Peer Listening Ports"
        description="Configure listening ports and automatic router port-forwarding negotiations"
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="BitTorrent Listening Port"
            value={form.listeningPort}
            onChange={(v) => update("listeningPort", v)}
            min={1}
            max={65535}
            hint="TCP/UDP port for inbound swarm peer connections (default: 51413 / 6881)"
          />

          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "0.75rem",
              justifyContent: "center",
            }}
          >
            <Toggle
              label="Enable UPnP / NAT-PMP"
              checked={form.upnpEnabled}
              onChange={(v) => update("upnpEnabled", v)}
              hint="Automatically negotiate gateway port forwarding with router"
            />

            <Toggle
              label="Enable IPv6 Dual-Stack"
              checked={form.enableIPv6}
              onChange={(v) => update("enableIPv6", v)}
              hint="Listens on both IPv4 and IPv6 network sockets"
            />
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Network Interface Binding &amp; VPN Kill Switch"
        description="Bind BitTorrent sockets to a dedicated physical/virtual network interface or VPN adapter"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          {customInterfaceMode ? (
            <TextInput
              label="Bind Network Interface"
              value={form.bindInterface}
              onChange={(v) => update("bindInterface", v)}
              placeholder="tun0, wg0, eth0, or IP address"
              hint="Interface name (e.g. tun0, wg0) or IP address to restrict all torrent traffic"
              rightElement={
                <button
                  type="button"
                  className="btn btn-outline btn-small"
                  onClick={() => {
                    setCustomInterfaceMode(false);
                    if (
                      !interfaces?.some((i) => i.name === form.bindInterface)
                    ) {
                      update("bindInterface", "");
                    }
                  }}
                  title="Choose from detected interfaces"
                >
                  Choose from list
                </button>
              }
            />
          ) : (
            <div>
              <SelectInput
                label="Bind Network Interface"
                value={
                  interfaces?.some((i) => i.name === form.bindInterface)
                    ? form.bindInterface
                    : form.bindInterface === ""
                      ? ""
                      : "__custom__"
                }
                onChange={(v) => {
                  if (v === "__custom__") {
                    setCustomInterfaceMode(true);
                  } else {
                    update("bindInterface", v);
                  }
                }}
                options={[
                  { value: "", label: "Any / All Interfaces (Default)" },
                  ...(interfaces || []).map((iface) => {
                    const addrStr =
                      iface.addresses && iface.addresses.length > 0
                        ? ` [${iface.addresses.join(", ")}]`
                        : "";
                    const typeStr = iface.isVpn ? "VPN Tunnel" : iface.type;
                    return {
                      value: iface.name,
                      label: `${iface.name}${addrStr} (${typeStr}) - ${iface.status}`,
                    };
                  }),
                  {
                    value: "__custom__",
                    label: "Custom Interface or IP Address...",
                  },
                ]}
                hint={
                  loadingInterfaces
                    ? "Loading available network interfaces..."
                    : "Select a physical or VPN network interface to restrict BitTorrent traffic, or choose Custom to enter an IP address"
                }
              />
              {interfaces?.find((i) => i.name === form.bindInterface) && (
                <div
                  style={{
                    marginTop: "-0.5rem",
                    marginBottom: "0.5rem",
                    padding: "0.4rem 0.75rem",
                    backgroundColor: "var(--bg-primary, #10111a)",
                    borderRadius: "6px",
                    border: "1px solid var(--border-light)",
                    fontSize: "0.8rem",
                    display: "flex",
                    gap: "1rem",
                    alignItems: "center",
                    flexWrap: "wrap",
                  }}
                >
                  {(() => {
                    const selected = interfaces.find(
                      (i) => i.name === form.bindInterface,
                    )!;
                    return (
                      <>
                        <span>
                          Type:{" "}
                          <strong
                            style={{
                              color: selected.isVpn
                                ? "var(--accent, #c8a84e)"
                                : "var(--text-secondary)",
                            }}
                          >
                            {selected.isVpn ? "🛡️ VPN Tunnel" : selected.type}
                          </strong>
                        </span>
                        <span>
                          Status:{" "}
                          <strong
                            style={{
                              color:
                                selected.status === "Up"
                                  ? "var(--success, #27ae60)"
                                  : "var(--danger, #e74c3c)",
                            }}
                          >
                            {selected.status}
                          </strong>
                        </span>
                        {selected.addresses && selected.addresses.length > 0 && (
                          <span>
                            IP: <code>{selected.addresses.join(", ")}</code>
                          </span>
                        )}
                        <button
                          type="button"
                          className="btn btn-outline btn-small"
                          style={{
                            marginLeft: "auto",
                            fontSize: "0.75rem",
                            padding: "0.15rem 0.5rem",
                          }}
                          onClick={() => setCustomInterfaceMode(true)}
                        >
                          Edit as text
                        </button>
                      </>
                    );
                  })()}
                </div>
              )}
            </div>
          )}

          <Toggle
            label="Enable Automated VPN Kill Switch"
            checked={form.enableVpnKillSwitch}
            onChange={(v) => update("enableVpnKillSwitch", v)}
            hint="Immediately drop all BitTorrent socket transfers in fail-closed mode if the bound VPN adapter drops"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="Socket Connection Limits &amp; Traffic Shaping"
        description="Tune active socket pools, per-host throttling, and IP QoS headers"
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="Maximum Global Connections"
            value={form.maxGlobalConnections}
            onChange={(v) => update("maxGlobalConnections", v)}
            min={10}
            max={5000}
            hint="Total simultaneous peer socket connections across all active swarms"
          />

          <NumberInput
            label="Max Connections Per Torrent"
            value={form.maxPerTorrentConnections}
            onChange={(v) => update("maxPerTorrentConnections", v)}
            min={1}
            max={500}
            hint="Maximum peer connections allocated per individual torrent swarm"
          />

          <NumberInput
            label="Max Upload Slots Per Torrent"
            value={form.maxUploadSlots}
            onChange={(v) => update("maxUploadSlots", v)}
            min={1}
            max={100}
            hint="Maximum concurrent unchoked peer upload transfers per torrent"
          />

          <NumberInput
            label="Max Connections Per Remote IP"
            value={form.maxConnectionsPerIp}
            onChange={(v) => update("maxConnectionsPerIp", v)}
            min={1}
            max={50}
            hint="Limit concurrent peer sockets originating from the same remote IP"
          />

          <NumberInput
            label="Max Half-Open Connections"
            value={form.maximumHalfOpenConnections}
            onChange={(v) => update("maximumHalfOpenConnections", v)}
            min={5}
            max={500}
            hint="Maximum pending TCP socket handshakes in connection queue"
          />

          <NumberInput
            label="IP Packet DSCP QoS Marking"
            value={form.peerDscp}
            onChange={(v) => update("peerDscp", v)}
            min={0}
            max={63}
            hint="Differentiated Services Code Point (DSCP) header priority marking (0-63)"
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default NetworkSettingsTab;
