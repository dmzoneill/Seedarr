import { useState, useEffect } from "react";
import { useNavigate } from "react-router";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { apiClient } from "../../api/client";
import { useToast } from "../../context/ToastContext";
import type { GeneralConfig, SslCertificateValidationResult } from "../../api/types";
import {
  SaveBar,
  Toggle,
  SelectInput,
  TextInput,
  NumberInput,
  SectionCard,
} from "./shared";

export function GeneralTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const save = useSaveGeneralConfig();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const [form, setForm] = useState<GeneralConfig>({
    id: 1,
    autoStart: false,
    themeStyle: "system",
    colorScheme: "auto",
    watchFolderEnabled: false,
    watchFolderPath: "",
    watchFolderScanIntervalSeconds: 10,
    watchFolderAutoStartTorrents: true,
    watchFolderDeleteAddedTorrents: false,
    port: 9898,
    bindAddress: "0.0.0.0",
    urlBase: "",
    authenticationEnabled: false,
    apiKey: "",
    enableSsl: false,
    sslPort: 9899,
    sslCertPath: "",
    sslKeyPath: "",
    sslCertPassword: "",
    redirectHttpToHttps: false,
  });
  const [dirty, setDirty] = useState(false);
  const [showApiKey, setShowApiKey] = useState(false);
  const [revealedApiKey, setRevealedApiKey] = useState<string | null>(null);
  const [loadingApiKey, setLoadingApiKey] = useState(false);
  const [copied, setCopied] = useState(false);
  const [testingSsl, setTestingSsl] = useState(false);
  const [sslTestResult, setSslTestResult] =
    useState<SslCertificateValidationResult | null>(null);

  useEffect(() => {
    if (config) {
      setForm({
        ...config,
        enableSsl: config.enableSsl ?? false,
        sslPort: config.sslPort ?? 9899,
        sslCertPath: config.sslCertPath ?? "",
        sslKeyPath: config.sslKeyPath ?? "",
        sslCertPassword: config.sslCertPassword ?? "",
        redirectHttpToHttps: config.redirectHttpToHttps ?? false,
      });
      setDirty(false);
      setRevealedApiKey(null);
      setShowApiKey(false);
    }
  }, [config]);

  const set = <K extends keyof GeneralConfig>(
    key: K,
    value: GeneralConfig[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  const handleToggleShowApiKey = async () => {
    if (showApiKey) {
      setShowApiKey(false);
      return;
    }
    if (revealedApiKey || !form.apiKey.includes("*")) {
      setShowApiKey(true);
      return;
    }
    try {
      setLoadingApiKey(true);
      const res = await apiClient.getApiKey();
      if (res?.apiKey) {
        setRevealedApiKey(res.apiKey);
        setShowApiKey(true);
      }
    } catch (_err) {
      showToast("Failed to retrieve unmasked API key", "error");
    } finally {
      setLoadingApiKey(false);
    }
  };

  const handleCopyApiKey = async () => {
    try {
      let keyToCopy = form.apiKey;
      if (revealedApiKey && form.apiKey.includes("*")) {
        keyToCopy = revealedApiKey;
      } else if (!keyToCopy || keyToCopy.includes("*")) {
        const res = await apiClient.getApiKey();
        keyToCopy = res.apiKey;
        setRevealedApiKey(res.apiKey);
      }
      if (!keyToCopy) {
        showToast("No API key available", "error");
        return;
      }
      await navigator.clipboard.writeText(keyToCopy);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
      showToast("API key copied to clipboard", "success");
    } catch (_err) {
      showToast("Failed to copy API key", "error");
    }
  };

  const generateApiKey = () => {
    const bytes = new Uint8Array(16);
    window.crypto.getRandomValues(bytes);
    const key = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join(
      "",
    );
    setRevealedApiKey(key);
    setShowApiKey(true);
    set("apiKey", key);
  };

  const handleTestSsl = async () => {
    setTestingSsl(true);
    setSslTestResult(null);
    try {
      const res = await apiClient.testSsl({
        enableSsl: form.enableSsl ?? false,
        sslPort: form.sslPort ?? 9899,
        sslCertPath: form.sslCertPath,
        sslKeyPath: form.sslKeyPath,
        sslCertPassword: form.sslCertPassword,
        bindAddress: form.bindAddress,
      });
      setSslTestResult(res);
    } catch (err) {
      setSslTestResult({
        isValid: false,
        subject: "",
        issuer: "",
        validFrom: "",
        validTo: "",
        thumbprint: "",
        hasPrivateKey: false,
        subjectAlternativeNames: [],
        handshakeSucceeded: false,
        message:
          err instanceof Error
            ? err.message
            : "SSL test failed to execute against the server endpoint.",
      });
    } finally {
      setTestingSsl(false);
    }
  };

  if (isLoading) return <div className="loading">Loading configuration...</div>;

  return (
    <div>
      <SaveBar
        dirty={dirty}
        isPending={save.isPending}
        isError={save.isError}
        isSuccess={save.isSuccess}
        error={save.error}
        onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })}
      />

      <SectionCard
        title="Application"
        description="Configure launch seeding, visual theme, and color schemes"
      >
        <Toggle
          label="Auto Start"
          checked={form.autoStart}
          onChange={(v) => set("autoStart", v)}
          hint="Start seeding all queued swarms immediately on launch"
        />
        <SelectInput
          label="Theme"
          value={form.themeStyle}
          onChange={(v) => set("themeStyle", v)}
          options={[
            { value: "system", label: "System Default" },
            { value: "dark", label: "Dark Charcoal" },
            { value: "light", label: "Light Theme" },
          ]}
          hint="Overall application appearance theme"
        />
        <SelectInput
          label="Color Scheme"
          value={form.colorScheme}
          onChange={(v) => set("colorScheme", v)}
          options={[
            { value: "auto", label: "Warm Gold (Default)" },
            { value: "blue", label: "Sapphire Blue" },
            { value: "green", label: "Emerald Green" },
            { value: "purple", label: "Amethyst Purple" },
          ]}
          hint="Accent brand highlight palette"
        />
      </SectionCard>

      <SectionCard
        title="Host & Networking"
        description="Configure web UI port, listening address, reverse proxy URL base, and API authentication"
      >
        <NumberInput
          label="Port"
          value={form.port}
          onChange={(v) => set("port", v)}
          min={1}
          max={65535}
          hint="HTTP port for Seedarr Web UI and REST API"
        />
        <TextInput
          label="Bind Address"
          value={form.bindAddress}
          onChange={(v) => set("bindAddress", v)}
          placeholder="0.0.0.0"
          hint="Listening IP address (* or 0.0.0.0 for all interfaces, 127.0.0.1 for local only)"
        />
        <TextInput
          label="URL Base"
          value={form.urlBase}
          onChange={(v) => set("urlBase", v)}
          placeholder="/seedarr"
          hint="Subdirectory prefix for reverse proxy setups (e.g. /seedarr)"
        />
        <Toggle
          label="Authentication"
          checked={form.authenticationEnabled}
          onChange={(v) => set("authenticationEnabled", v)}
          hint="Require user login credentials for Web UI and API access"
        />
        <TextInput
          label="API Key (X-Api-Key)"
          type={showApiKey ? "text" : "password"}
          value={
            showApiKey
              ? (revealedApiKey || (form.apiKey.includes("*") ? "" : form.apiKey))
              : (revealedApiKey || form.apiKey)
          }
          onChange={(v) => {
            setRevealedApiKey(null);
            set("apiKey", v);
          }}
          hint="Secret token for Arr apps (Radarr/Sonarr) and REST API access"
          rightElement={
            <button
              type="button"
              className="btn btn-outline"
              onClick={handleToggleShowApiKey}
              style={{
                whiteSpace: "nowrap",
                height: "36px",
                padding: "0 0.75rem",
              }}
              disabled={loadingApiKey}
              title={showApiKey ? "Hide API Key" : "Show unmasked API Key"}
            >
              {loadingApiKey ? "..." : showApiKey ? "🙈 Hide" : "👁️ Show"}
            </button>
          }
        />
        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            marginTop: "-0.5rem",
            marginBottom: "1rem",
            flexWrap: "wrap",
          }}
        >
          <button
            type="button"
            className="btn btn-outline"
            onClick={handleCopyApiKey}
            style={{ whiteSpace: "nowrap" }}
          >
            📋 {copied ? "Copied!" : "Copy"}
          </button>
          <button
            type="button"
            className="btn btn-outline"
            onClick={generateApiKey}
            style={{ whiteSpace: "nowrap" }}
          >
            🔄 Regenerate
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => navigate("/system/api")}
            style={{ whiteSpace: "nowrap" }}
          >
            📖 API Docs (OpenAPI)
          </button>
        </div>
      </SectionCard>

      <SectionCard
        title="SSL & HTTPS Encryption"
        description="Configure TLS/SSL certificate encryption, dual-listener HTTPS port, and connection verification."
      >
        <div
          style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
        >
          <Toggle
            label="Enable SSL (HTTPS)"
            checked={form.enableSsl ?? false}
            onChange={(v) => set("enableSsl", v)}
            hint="Activate secure HTTPS web server endpoint with TLS certificate encryption"
          />

          {form.enableSsl && (
            <>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
                  gap: "1rem",
                }}
              >
                <NumberInput
                  label="SSL / HTTPS Port"
                  value={form.sslPort ?? 9899}
                  onChange={(v) => set("sslPort", v)}
                  min={1}
                  max={65535}
                  hint="Dedicated port for secure HTTPS traffic (default: 9899)"
                />

                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    paddingTop: "1.5rem",
                  }}
                >
                  <Toggle
                    label="Redirect HTTP to HTTPS"
                    checked={form.redirectHttpToHttps ?? false}
                    onChange={(v) => set("redirectHttpToHttps", v)}
                    hint="Automatically redirect plain HTTP requests to the HTTPS port"
                  />
                </div>
              </div>

              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
                  gap: "1rem",
                }}
              >
                <TextInput
                  label="Certificate Path (.pfx, .crt, .pem)"
                  value={form.sslCertPath ?? ""}
                  onChange={(v) => set("sslCertPath", v)}
                  hint="Path to PKCS#12 (.pfx/.p12) or PEM (.crt/.pem) certificate. Leave blank for auto-generated self-signed certificate."
                />

                <TextInput
                  label="Private Key Path (.key) (Optional for PEM)"
                  value={form.sslKeyPath ?? ""}
                  onChange={(v) => set("sslKeyPath", v)}
                  hint="Path to separate PEM private key (.key). Leave blank if key is bundled in certificate or PFX."
                />

                <TextInput
                  label="Certificate Password (Optional for PFX)"
                  value={form.sslCertPassword ?? ""}
                  onChange={(v) => set("sslCertPassword", v)}
                  type="password"
                  hint="Decryption passphrase if certificate file is password-protected"
                />
              </div>

              {/* Test Certificate Action & Live Verification Result */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: "1rem",
                  flexWrap: "wrap",
                  paddingTop: "0.5rem",
                }}
              >
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={handleTestSsl}
                  disabled={testingSsl}
                  style={{ minWidth: "160px" }}
                >
                  {testingSsl
                    ? "Testing Certificate..."
                    : "🔒 Test SSL Connection"}
                </button>

                <span
                  style={{ fontSize: "0.82rem", color: "var(--text-muted)" }}
                >
                  Validates certificate cryptographic structure, private key,
                  SAN domains, and active HTTPS handshake.
                </span>
              </div>

              {sslTestResult && (
                <div
                  style={{
                    padding: "1rem 1.25rem",
                    borderRadius: "8px",
                    backgroundColor: sslTestResult.isValid
                      ? "rgba(46, 204, 113, 0.08)"
                      : "rgba(231, 76, 60, 0.08)",
                    border: sslTestResult.isValid
                      ? "1px solid rgba(46, 204, 113, 0.3)"
                      : "1px solid rgba(231, 76, 60, 0.3)",
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.5rem",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "0.5rem",
                    }}
                  >
                    <span style={{ fontSize: "1.1rem" }}>
                      {sslTestResult.isValid ? "✓" : "⚠️"}
                    </span>
                    <strong
                      style={{
                        color: sslTestResult.isValid
                          ? "var(--success, #2ecc71)"
                          : "var(--danger, #e74c3c)",
                        fontSize: "0.95rem",
                      }}
                    >
                      {sslTestResult.isValid
                        ? "✓ SSL Certificate & Configuration Valid"
                        : "⚠️ SSL Certificate Validation Issue"}
                    </strong>
                  </div>

                  <p
                    style={{
                      margin: 0,
                      fontSize: "0.86rem",
                      color: "var(--text-primary)",
                    }}
                  >
                    {sslTestResult.message}
                  </p>

                  {sslTestResult.isValid && (
                    <div
                      style={{
                        display: "grid",
                        gridTemplateColumns:
                          "repeat(auto-fit, minmax(220px, 1fr))",
                        gap: "0.75rem",
                        marginTop: "0.5rem",
                        fontSize: "0.8rem",
                        color: "var(--text-secondary)",
                        backgroundColor: "rgba(0, 0, 0, 0.2)",
                        padding: "0.75rem 1rem",
                        borderRadius: "6px",
                      }}
                    >
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          Subject
                        </span>
                        <span
                          style={{
                            color: "var(--text-primary)",
                            wordBreak: "break-all",
                          }}
                        >
                          {sslTestResult.subject || "N/A"}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          Issuer
                        </span>
                        <span
                          style={{
                            color: "var(--text-primary)",
                            wordBreak: "break-all",
                          }}
                        >
                          {sslTestResult.issuer || "N/A"}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          Valid Until
                        </span>
                        <span style={{ color: "var(--accent)" }}>
                          {sslTestResult.validTo
                            ? new Date(
                                sslTestResult.validTo,
                              ).toLocaleDateString()
                            : "N/A"}
                        </span>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            display: "block",
                          }}
                        >
                          Private Key
                        </span>
                        <span
                          style={{
                            color: sslTestResult.hasPrivateKey
                              ? "var(--success, #2ecc71)"
                              : "var(--danger)",
                          }}
                        >
                          {sslTestResult.hasPrivateKey
                            ? "Present (Verified)"
                            : "Missing"}
                        </span>
                      </div>
                      {sslTestResult.subjectAlternativeNames?.length > 0 && (
                        <div style={{ gridColumn: "1 / -1" }}>
                          <span
                            style={{
                              color: "var(--text-muted)",
                              display: "block",
                              marginBottom: "0.25rem",
                            }}
                          >
                            Subject Alternative Names (SANs)
                          </span>
                          <div
                            style={{
                              display: "flex",
                              gap: "0.4rem",
                              flexWrap: "wrap",
                            }}
                          >
                            {sslTestResult.subjectAlternativeNames.map(
                              (san) => (
                                <span
                                  key={san}
                                  style={{
                                    padding: "0.15rem 0.45rem",
                                    borderRadius: "4px",
                                    backgroundColor:
                                      "rgba(255, 209, 102, 0.12)",
                                    color: "var(--accent)",
                                    fontSize: "0.75rem",
                                  }}
                                >
                                  {san}
                                </span>
                              ),
                            )}
                          </div>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}

              <div
                style={{
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  backgroundColor: "rgba(255, 209, 102, 0.08)",
                  border: "1px solid rgba(255, 209, 102, 0.2)",
                  fontSize: "0.85rem",
                  color: "var(--text-secondary)",
                  lineHeight: "1.4",
                }}
              >
                <strong style={{ color: "var(--accent)" }}>
                  TLS Certificate Provisioning Note:
                </strong>{" "}
                When Certificate Path is left empty, Seedarr automatically
                generates, signs, and caches an internal 2048-bit RSA self-signed
                certificate in the application configuration directory. Web
                server port and SSL changes take effect upon restarting the
                application daemon or container.
              </div>
            </>
          )}
        </div>
      </SectionCard>

      <SectionCard
        title="Watch Folder Automation"
        description="Automatically scan local folders for newly added .torrent files"
      >
        <Toggle
          label="Enabled"
          checked={form.watchFolderEnabled}
          onChange={(v) => set("watchFolderEnabled", v)}
          hint="Auto-import .torrent files discovered in the watch folder"
        />
        <TextInput
          label="Path"
          value={form.watchFolderPath}
          onChange={(v) => set("watchFolderPath", v)}
          placeholder="/watch"
          disabled={!form.watchFolderEnabled}
          hint="Absolute filesystem path to monitor"
        />
        <NumberInput
          label="Scan Interval"
          value={form.watchFolderScanIntervalSeconds}
          onChange={(v) => set("watchFolderScanIntervalSeconds", v)}
          min={1}
          suffix="seconds"
          disabled={!form.watchFolderEnabled}
          hint="Frequency to scan for new incoming torrent files"
        />
        <Toggle
          label="Auto Start Torrents"
          checked={form.watchFolderAutoStartTorrents}
          onChange={(v) => set("watchFolderAutoStartTorrents", v)}
          hint="Begin seeding torrents immediately once imported from the watch folder"
        />
        <Toggle
          label="Delete After Adding"
          checked={form.watchFolderDeleteAddedTorrents}
          onChange={(v) => set("watchFolderDeleteAddedTorrents", v)}
          hint="Remove .torrent file from the watch directory after successful import"
        />
      </SectionCard>
    </div>
  );
}
