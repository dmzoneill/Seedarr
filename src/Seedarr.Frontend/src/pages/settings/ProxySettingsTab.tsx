import { useState, useEffect } from "react";
import {
  useNetworkConfig,
  useSaveNetworkConfig,
  useTestProxy,
} from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  NumberInput,
  TextInput,
  SelectInput,
  Toggle,
} from "./shared";
import { trackNetworkConfigSave } from "../../utils/analytics";

export function ProxySettingsTab() {
  const { data: config, isLoading } = useNetworkConfig();
  const saveMutation = useSaveNetworkConfig();
  const testMutation = useTestProxy();

  const [form, setForm] = useState({
    proxyType: "none",
    proxyHost: "",
    proxyPort: 8080,
    proxyAuthEnabled: false,
    proxyUsername: "",
    proxyPassword: "",
    anonymousMode: false,
    forceProxy: false,
  });

  const [dirty, setDirty] = useState(false);
  const [testResult, setTestResult] = useState<{
    success: boolean;
    message: string;
    remoteDnsVerified?: boolean;
  } | null>(null);

  useEffect(() => {
    if (config) {
      setForm({
        proxyType: config.proxyType || "none",
        proxyHost: config.proxyHost || "",
        proxyPort: config.proxyPort ?? 8080,
        proxyAuthEnabled: config.proxyAuthEnabled ?? false,
        proxyUsername: config.proxyUsername || "",
        proxyPassword: config.proxyPassword || "",
        anonymousMode: config.anonymousMode ?? false,
        forceProxy: config.forceProxy ?? false,
      });
      setDirty(false);
    }
  }, [config]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
    setTestResult(null);
  };

  const handleSave = () => {
    if (!config) return;
    trackNetworkConfigSave({
      has_proxy: form.proxyType !== "none",
      proxy_type: form.proxyType,
    });
    saveMutation.mutate(
      {
        ...config,
        proxyType: form.proxyType,
        proxyHost: form.proxyHost,
        proxyPort: form.proxyPort,
        proxyAuthEnabled: form.proxyAuthEnabled,
        proxyUsername: form.proxyUsername,
        proxyPassword: form.proxyPassword,
        anonymousMode: form.anonymousMode,
        forceProxy: form.forceProxy,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  const handleTestProxy = async () => {
    if (!isProxyActive || !form.proxyHost) return;
    setTestResult(null);
    try {
      const res = await testMutation.mutateAsync({
        proxyType: form.proxyType,
        proxyHost: form.proxyHost,
        proxyPort: form.proxyPort,
        proxyAuthEnabled: form.proxyAuthEnabled,
        proxyUsername: form.proxyUsername,
        proxyPassword: form.proxyPassword,
      });
      setTestResult(res);
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : "Proxy test connection failed.";
      setTestResult({
        success: false,
        message,
      });
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading proxy configuration...
      </div>
    );
  }

  const isProxyActive = form.proxyType !== "none";

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

      <SectionCard
        title="Outbound SOCKS5 / HTTP Proxy Tunnel"
        description="Route BitTorrent tracker queries and peer socket connections through a remote proxy"
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <SelectInput
            label="Proxy Protocol Type"
            value={form.proxyType}
            onChange={(v) => update("proxyType", v)}
            options={[
              { value: "none", label: "None (Direct Connection)" },
              { value: "socks5", label: "SOCKS5 Proxy" },
              { value: "socks5h", label: "SOCKS5h (Remote DNS)" },
              { value: "http", label: "HTTP Proxy" },
            ]}
          />

          <TextInput
            label="Proxy Hostname / IP"
            value={form.proxyHost}
            onChange={(v) => update("proxyHost", v)}
            disabled={!isProxyActive}
            placeholder="proxy.example.com"
            hint="Proxy server hostname or IP address"
          />

          <NumberInput
            label="Proxy Port"
            value={form.proxyPort}
            onChange={(v) => update("proxyPort", v)}
            disabled={!isProxyActive}
            min={1}
            max={65535}
            hint="Target proxy listening port (e.g. 1080 or 8080)"
          />
        </div>

        {isProxyActive && (
          <div
            style={{
              marginTop: "1rem",
              borderTop: "1px solid var(--border-light)",
              paddingTop: "1rem",
            }}
          >
            <Toggle
              label="Enable Proxy Authentication"
              checked={form.proxyAuthEnabled}
              onChange={(v) => update("proxyAuthEnabled", v)}
            />

            {form.proxyAuthEnabled && (
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
                  gap: "1rem",
                  marginTop: "0.75rem",
                }}
              >
                <TextInput
                  label="Proxy Username"
                  value={form.proxyUsername}
                  onChange={(v) => update("proxyUsername", v)}
                />

                <TextInput
                  label="Proxy Password"
                  value={form.proxyPassword}
                  onChange={(v) => update("proxyPassword", v)}
                  type="password"
                />
              </div>
            )}

            <div
              style={{
                marginTop: "1.25rem",
                borderTop: "1px solid var(--border-light)",
                paddingTop: "1rem",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "1rem",
                  flexWrap: "wrap",
                }}
              >
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={handleTestProxy}
                  disabled={testMutation.isPending || !form.proxyHost}
                  style={{ fontSize: "0.85rem" }}
                >
                  {testMutation.isPending
                    ? "Testing Proxy Connection..."
                    : "Test Proxy Connection"}
                </button>
                {testMutation.isPending && (
                  <span
                    style={{
                      fontSize: "0.85rem",
                      color: "var(--text-muted)",
                    }}
                  >
                    Connecting and verifying handshake...
                  </span>
                )}
              </div>

              {testResult && (
                <div
                  style={{
                    marginTop: "0.75rem",
                    padding: "10px 14px",
                    borderRadius: "6px",
                    fontSize: "13px",
                    backgroundColor: testResult.success
                      ? "var(--success-bg, rgba(46, 204, 113, 0.15))"
                      : "var(--danger-bg-alert, rgba(231, 76, 60, 0.15))",
                    border: `1px solid ${
                      testResult.success
                        ? "var(--success, #2ecc71)"
                        : "var(--danger-border-alert, #e74c3c)"
                    }`,
                    color: testResult.success
                      ? "var(--success, #2ecc71)"
                      : "var(--danger, #e74c3c)",
                  }}
                >
                  {testResult.success ? "✓ " : "✕ "} {testResult.message}
                </div>
              )}
            </div>
          </div>
        )}
      </SectionCard>

      <SectionCard
        title="Privacy & Anonymous Routing Policy"
        description="Enforce strict proxy routing and prevent IP address leaks"
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <Toggle
            label="Anonymous Mode"
            checked={form.anonymousMode}
            onChange={(v) => update("anonymousMode", v)}
            hint="Strip client fingerprint headers, hide user-agent strings, and prevent direct DNS resolution"
          />

          <Toggle
            label="Strict Proxy Enforcement (Kill Switch)"
            checked={form.forceProxy}
            onChange={(v) => update("forceProxy", v)}
            disabled={!isProxyActive}
            hint="Drop all peer connections and tracker announcements if the proxy connection fails"
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default ProxySettingsTab;
