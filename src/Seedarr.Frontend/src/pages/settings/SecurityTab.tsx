import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { apiClient } from "../../api/client";
import { useToast } from "../../context/ToastContext";
import {
  IdentityProviderDefinition,
  IdentityProviderType,
  GeneralConfig,
} from "../../api/types";
import {
  useBlocklistStatus,
  useUpdateBlocklistConfig,
  useSyncBlocklist,
  testBlocklistIp,
} from "../../api/blocklist";
import { NumberInput, SaveBar, SectionCard, SelectInput, TextInput, Toggle } from "./shared";
import {
  getStoredIdleTimeout,
  setStoredIdleTimeout,
  TIMEOUT_OPTIONS,
} from "../../hooks/useIdleTimer";

const PROVIDER_TEMPLATES: Record<
  string,
  Partial<IdentityProviderDefinition>
> = {
  authentik: {
    providerId: "authentik",
    name: "Authentik",
    providerType: IdentityProviderType.Oidc,
    issuerUrl: "https://auth.example.com/application/o/seedarr/",
    scopes: "openid profile email groups",
    buttonText: "Sign in with Authentik",
    roleMappingRules:
      '{"Admin":"^(admin|authentik Admins|infrastructure)$","Operator":"^(operators|media-managers)$"}',
  },
  keycloak: {
    providerId: "keycloak",
    name: "Keycloak",
    providerType: IdentityProviderType.Oidc,
    issuerUrl: "https://keycloak.example.com/realms/master",
    scopes: "openid profile email roles",
    buttonText: "Sign in with Keycloak",
    roleMappingRules:
      '{"Admin":"^(realm-admin|seedarr-admin)$","Operator":"^(seedarr-operator)$"}',
  },
  authelia: {
    providerId: "authelia",
    name: "Authelia",
    providerType: IdentityProviderType.Oidc,
    issuerUrl: "https://auth.example.com",
    scopes: "openid profile email groups",
    buttonText: "Sign in with Authelia",
    roleMappingRules: '{"Admin":"^(admins|devops)$"}',
  },
  google: {
    providerId: "google",
    name: "Google",
    providerType: IdentityProviderType.Social,
    issuerUrl: "https://accounts.google.com",
    scopes: "openid profile email",
    buttonText: "Sign in with Google",
  },
  github: {
    providerId: "github",
    name: "GitHub",
    providerType: IdentityProviderType.Social,
    issuerUrl: "https://github.com",
    scopes: "read:user user:email",
    buttonText: "Sign in with GitHub",
  },
  apple: {
    providerId: "apple",
    name: "Apple",
    providerType: IdentityProviderType.Social,
    issuerUrl: "https://appleid.apple.com",
    scopes: "name email",
    buttonText: "Sign in with Apple",
  },
  saml: {
    providerId: "enterprise-saml",
    name: "Enterprise SAML 2.0",
    providerType: IdentityProviderType.Saml,
    issuerUrl: "https://idp.example.com/sso/saml",
    metadataUrl: "https://idp.example.com/metadata.xml",
    certificate:
      "-----BEGIN CERTIFICATE-----\nMIICXjCCAcegAwIBAgIJAP9...\n-----END CERTIFICATE-----",
    buttonText: "Single Sign-On (SAML)",
  },
};

export function SecurityTab() {
  const navigate = useNavigate();
  const { showToast } = useToast();
  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();

  const [idleTimeout, setIdleTimeout] = useState<number>(() =>
    getStoredIdleTimeout(),
  );

  const handleIdleTimeoutChange = (seconds: number) => {
    setIdleTimeout(seconds);
    setStoredIdleTimeout(seconds);
    showToast("Session inactivity lock timeout updated", "info");
  };

  const [form, setForm] = useState({
    authenticationEnabled: false,
    apiKey: "",
    csrfProtectionEnabled: true,
    hostHeaderValidationEnabled: false,
    allowedHosts: "",
    terminalAccessEnabled: true,
  });

  const [providers, setProviders] = useState<IdentityProviderDefinition[]>([]);
  const [loadingProviders, setLoadingProviders] = useState(false);
  const [editingProvider, setEditingProvider] =
    useState<Partial<IdentityProviderDefinition> | null>(null);
  const [isNewProvider, setIsNewProvider] = useState(false);
  const [testResult, setTestResult] = useState<{
    success: boolean;
    message: string;
  } | null>(null);
  const [testing, setTesting] = useState(false);

  const [copied, setCopied] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [showSecret, setShowSecret] = useState(false);
  const [showApiKey, setShowApiKey] = useState(false);
  const [revealedApiKey, setRevealedApiKey] = useState<string | null>(null);
  const [loadingApiKey, setLoadingApiKey] = useState(false);
  const [showRegenerateModal, setShowRegenerateModal] = useState(false);

  // Peer IP Blocklist state
  const { data: blocklistData } = useBlocklistStatus();
  const updateBlocklistMutation = useUpdateBlocklistConfig();
  const syncBlocklistMutation = useSyncBlocklist();

  const [blocklistForm, setBlocklistForm] = useState({
    enabled: false,
    url: "",
    autoUpdateEnabled: true,
    autoUpdateIntervalDays: 1,
  });
  const [blocklistDirty, setBlocklistDirty] = useState(false);
  const [customInterval, setCustomInterval] = useState(false);

  // IP Diagnostics state
  const [diagnosticsOpen, setDiagnosticsOpen] = useState(false);
  const [testIpInput, setTestIpInput] = useState("");
  const [testingIp, setTestingIp] = useState(false);
  const [ipTestResult, setIpTestResult] = useState<{
    isBlocked: boolean;
    rule: string | null;
  } | null>(null);

  useEffect(() => {
    if (blocklistData) {
      setBlocklistForm({
        enabled: blocklistData.enabled ?? false,
        url: blocklistData.url ?? "",
        autoUpdateEnabled: blocklistData.autoUpdateEnabled ?? true,
        autoUpdateIntervalDays: blocklistData.autoUpdateIntervalDays ?? 1,
      });
      const isPreset = [1, 3, 7, 14, 30].includes(
        blocklistData.autoUpdateIntervalDays ?? 1,
      );
      setCustomInterval(!isPreset);
      setBlocklistDirty(false);
    }
  }, [blocklistData]);

  const updateBlocklist = <K extends keyof typeof blocklistForm>(
    key: K,
    val: (typeof blocklistForm)[K],
  ) => {
    setBlocklistForm((prev) => ({ ...prev, [key]: val }));
    setBlocklistDirty(true);
  };

  const handleSaveBlocklist = async () => {
    try {
      await updateBlocklistMutation.mutateAsync(blocklistForm);
      setBlocklistDirty(false);
      showToast("Peer blocklist configuration saved", "success");
    } catch (err: any) {
      showToast(
        err?.message || "Failed to save blocklist configuration",
        "error",
      );
    }
  };

  const handleSyncBlocklist = async () => {
    try {
      const res = await syncBlocklistMutation.mutateAsync();
      if (res.success) {
        showToast(
          `Blocklist synchronized: ${res.ruleCount.toLocaleString()} rules active`,
          "success",
        );
      } else {
        showToast(
          `Blocklist sync warning: ${res.status || res.message}`,
          "warning",
        );
      }
    } catch (err: any) {
      showToast(err?.message || "Failed to synchronize blocklist", "error");
    }
  };

  const handleCheckIp = async () => {
    if (!testIpInput.trim()) return;
    try {
      setTestingIp(true);
      setIpTestResult(null);
      const res = await testBlocklistIp(testIpInput.trim());
      setIpTestResult(res);
    } catch (err: any) {
      showToast(
        err?.message || "Failed to verify IP against blocklist",
        "error",
      );
    } finally {
      setTestingIp(false);
    }
  };

  useEffect(() => {
    if (config) {
      setForm({
        authenticationEnabled: config.authenticationEnabled ?? false,
        apiKey: config.apiKey ?? "",
        csrfProtectionEnabled: config.csrfProtectionEnabled ?? true,
        hostHeaderValidationEnabled:
          config.hostHeaderValidationEnabled ?? false,
        allowedHosts: config.allowedHosts ?? "",
        terminalAccessEnabled: config.terminalAccessEnabled ?? true,
      });

      setDirty(false);
      setRevealedApiKey(null);
      setShowApiKey(false);
    }
  }, [config]);

  useEffect(() => {
    loadProviders();
  }, []);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        if (showRegenerateModal) {
          e.preventDefault();
          e.stopPropagation();
          e.stopImmediatePropagation();
          setShowRegenerateModal(false);
          return;
        }
        if (editingProvider) {
          e.preventDefault();
          e.stopPropagation();
          e.stopImmediatePropagation();
          setEditingProvider(null);
          setShowSecret(false);
          return;
        }
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [editingProvider, showRegenerateModal]);

  const loadProviders = async () => {
    try {
      setLoadingProviders(true);
      const list = await apiClient.getIdProviders();
      setProviders(list || []);
    } catch {
      // Ignore
    } finally {
      setLoadingProviders(false);
    }
  };

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: val }));
    setDirty(true);
  };

  const generateApiKey = () => {
    const bytes = new Uint8Array(16);
    window.crypto.getRandomValues(bytes);
    const key = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join(
      "",
    );
    setRevealedApiKey(key);
    setShowApiKey(true);
    update("apiKey", key);
  };

  const handleToggleShowApiKey = async () => {
    if (showApiKey) {
      setShowApiKey(false);
      return;
    }

    if (revealedApiKey || (!form.apiKey.includes("*") && form.apiKey)) {
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
    if (!navigator.clipboard?.writeText) {
      showToast("Clipboard API not available", "error");
      return;
    }

    try {
      let keyToCopy = form.apiKey;
      if (revealedApiKey) {
        keyToCopy = revealedApiKey;
      } else if (!keyToCopy || keyToCopy.includes("*")) {
        const res = await apiClient.getApiKey();
        keyToCopy = res.apiKey;
        setRevealedApiKey(res.apiKey);
      }

      if (!keyToCopy || keyToCopy.includes("*")) {
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

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        authenticationEnabled: form.authenticationEnabled,
        apiKey: form.apiKey,
        csrfProtectionEnabled: form.csrfProtectionEnabled,
        hostHeaderValidationEnabled: form.hostHeaderValidationEnabled,
        allowedHosts: form.allowedHosts,
        terminalAccessEnabled: form.terminalAccessEnabled,
      } as GeneralConfig,
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  const handleOpenAdd = (templateKey = "authentik") => {
    const template =
      PROVIDER_TEMPLATES[templateKey] || PROVIDER_TEMPLATES.authentik;
    setEditingProvider({
      ...template,
      isEnabled: true,
      clientId: "",
      clientSecret: "",
    });
    setIsNewProvider(true);
    setTestResult(null);
    setShowSecret(false);
  };

  const handleOpenEdit = (p: IdentityProviderDefinition) => {
    setEditingProvider({ ...p });
    setIsNewProvider(false);
    setTestResult(null);
    setShowSecret(false);
  };

  const handleSaveProvider = async () => {
    if (
      !editingProvider ||
      !editingProvider.providerId ||
      !editingProvider.name
    )
      return;

    try {
      if (isNewProvider) {
        await apiClient.createIdProvider(editingProvider);
      } else if (editingProvider.id) {
        await apiClient.updateIdProvider(editingProvider.id, editingProvider);
      }
      setEditingProvider(null);
      setShowSecret(false);
      await loadProviders();
      showToast("Identity provider saved successfully", "success");
    } catch (err: any) {
      showToast(
        err?.message || "Failed to save identity provider",
        "error",
      );
    }
  };

  const handleDeleteProvider = async (p: IdentityProviderDefinition) => {
    const ok = window.confirm(
      `Are you sure you want to remove identity provider "${p.name}"?`,
    );
    if (!ok) return;

    try {
      await apiClient.deleteIdProvider(p.id);
      await loadProviders();
      showToast("Identity provider removed", "success");
    } catch (err: any) {
      showToast(
        err?.message || "Failed to delete identity provider",
        "error",
      );
    }
  };

  const handleTestConnection = async () => {
    if (!editingProvider) return;
    try {
      setTesting(true);
      setTestResult(null);
      const res = await apiClient.testIdProvider(editingProvider);
      setTestResult(res);
    } catch (err: any) {
      setTestResult({
        success: false,
        message: err?.message || "Connection failed",
      });
    } finally {
      setTesting(false);
    }
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading security parameters...
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

      {/* Card 1: Authentication & Access Gate */}
      <SectionCard
        title="Authentication & Access Gate"
        description="Configure user login protection and authentication gate"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label="Enable Web UI Authentication"
            checked={form.authenticationEnabled}
            onChange={(v) => update("authenticationEnabled", v)}
            hint="Require login credentials before accessing the Web UI"
          />

          <SelectInput
            label="Session Inactivity Lock Timeout"
            value={String(idleTimeout)}
            onChange={(v) => handleIdleTimeoutChange(Number(v))}
            options={TIMEOUT_OPTIONS.map((opt) => ({
              value: String(opt.value),
              label: opt.label,
            }))}
            hint="Automatically lock screen and require password/PIN re-entry after inactivity without discarding active form state"
          />

          {form.authenticationEnabled && (
            <div
              style={{
                backgroundColor: "var(--bg-primary)",
                padding: "1rem",
                borderRadius: "6px",
                border: "1px solid var(--border)",
              }}
            >
              <div
                style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}
              >
                When Web UI Authentication is enabled, all requests will require valid
                session credentials or basic authentication. Ensure at least one Identity
                Provider is configured or default admin credentials are known before locking
                down access.
              </div>
            </div>
          )}
        </div>
      </SectionCard>

      {/* Card 2: Web UI Security & Request Origin Protection */}
      <SectionCard
        title="Web UI Security & Request Origin Protection"
        description="Prevent CSRF, DNS rebinding attacks, and unauthorized origin access"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <Toggle
            label="Enable CSRF Protection"
            checked={form.csrfProtectionEnabled}
            onChange={(v) => update("csrfProtectionEnabled", v)}
            hint="Enforces strict Origin and Referer header checks on state-changing requests"
          />

          <Toggle
            label="Enable Strict Host Header Validation"
            checked={form.hostHeaderValidationEnabled}
            onChange={(v) => update("hostHeaderValidationEnabled", v)}
            hint="Prevents DNS rebinding attacks by validating the Host HTTP header against whitelist"
          />

          <Toggle
            label="Terminal Access"
            checked={form.terminalAccessEnabled}
            onChange={(v) => update("terminalAccessEnabled", v)}
            hint="Allow interactive command execution and console terminal access via Web UI"
          />

          {form.hostHeaderValidationEnabled && (
            <TextInput
              label="Allowed Host Headers Whitelist"
              value={form.allowedHosts}
              onChange={(v) => update("allowedHosts", v)}
              placeholder="localhost, 127.0.0.1, seedarr.local"
              hint="Comma-separated list of allowed hostnames or IP addresses (e.g. localhost, seedarr.local, 192.168.1.100)"
            />
          )}
        </div>
      </SectionCard>

      {/* Card 3: Identity Providers & Single Sign-On (SSO) */}
      <SectionCard
        title="Identity Providers & Single Sign-On (SSO)"
        description="Integrate self-hosted IdPs and social identity providers for single sign-on authentication"
      >
        <div
          style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "0.5rem",
            }}
          >
            <div
              style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}
            >
              Configured Identity Providers ({providers.length})
            </div>
            <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("authentik")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Authentik
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("keycloak")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Keycloak
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("authelia")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Authelia
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("google")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Google
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("github")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + GitHub
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("apple")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + Apple
              </button>
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => handleOpenAdd("saml")}
                style={{ fontSize: "0.8rem", padding: "4px 10px" }}
              >
                + SAML 2.0
              </button>
            </div>
          </div>

          {loadingProviders ? (
            <div
              style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}
            >
              Loading identity providers...
            </div>
          ) : providers.length === 0 ? (
            <div
              style={{
                backgroundColor: "var(--bg-primary)",
                padding: "1.5rem",
                borderRadius: "6px",
                border: "1px dashed var(--border)",
                textAlign: "center",
                color: "var(--text-secondary)",
                fontSize: "0.9rem",
              }}
            >
              No identity providers configured. Click a quick-add button above to configure SSO.
            </div>
          ) : (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.75rem",
              }}
            >
              {providers.map((p) => (
                <div
                  key={p.id}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "12px 16px",
                    backgroundColor: "var(--bg-primary)",
                    borderRadius: "6px",
                    border: "1px solid var(--border)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "12px",
                    }}
                  >
                    <div
                      style={{
                        width: "10px",
                        height: "10px",
                        borderRadius: "50%",
                        backgroundColor: p.isEnabled ? "#10B981" : "#6B7280",
                      }}
                    />
                    <div>
                      <div
                        style={{
                          color: "var(--text-primary)",
                          fontWeight: 600,
                          fontSize: "0.95rem",
                        }}
                      >
                        {p.name}
                      </div>
                      <div
                        style={{
                          color: "var(--text-secondary)",
                          fontSize: "0.8rem",
                        }}
                      >
                        Type:{" "}
                        {p.providerType === IdentityProviderType.Oidc
                          ? "OIDC"
                          : p.providerType === IdentityProviderType.Saml
                            ? "SAML 2.0"
                            : p.providerType === IdentityProviderType.Social
                              ? "Social"
                              : "ForwardAuth"}{" "}
                        | ID: {p.providerId}{" "}
                        {p.issuerUrl ? `| ${p.issuerUrl}` : ""}
                      </div>
                    </div>
                  </div>
                  <div style={{ display: "flex", gap: "0.5rem" }}>
                    <button
                      type="button"
                      className="btn btn-outline"
                      onClick={() => handleOpenEdit(p)}
                      style={{ fontSize: "0.8rem", padding: "4px 10px" }}
                    >
                      ✏️ Edit
                    </button>
                    <button
                      type="button"
                      className="btn btn-danger"
                      onClick={() => handleDeleteProvider(p)}
                      style={{ fontSize: "0.8rem", padding: "4px 10px" }}
                    >
                      🗑️ Delete
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </SectionCard>

      {/* Card 4: REST API Key Security */}
      <SectionCard
        title="REST API Key Security"
        description="Master API authentication token required for third-party scripts, CLI, and integrations"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <div
            style={{ display: "flex", gap: "0.5rem", alignItems: "flex-end" }}
          >
            <div style={{ flex: 1 }}>
              <TextInput
                label="API Key"
                type={showApiKey ? "text" : "password"}
                value={
                  showApiKey
                    ? (revealedApiKey || (form.apiKey.includes("*") ? "" : form.apiKey))
                    : (revealedApiKey || form.apiKey)
                }
                onChange={(v) => {
                  setRevealedApiKey(null);
                  update("apiKey", v);
                }}
                hint="Pass this key in the X-Api-Key HTTP header for all programmatic API requests"
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
                    title={
                      showApiKey
                        ? "Hide API Key"
                        : "Show Unmasked API Key"
                    }
                    aria-label={
                      showApiKey
                        ? "Hide API Key"
                        : "Show Unmasked API Key"
                    }
                    disabled={loadingApiKey}
                  >
                    {loadingApiKey ? "..." : showApiKey ? "🙈 Hide" : "👁️ Show"}
                  </button>
                }
              />
            </div>
            <button
              type="button"
              className="btn btn-outline"
              onClick={handleCopyApiKey}
              style={{ marginBottom: "0.25rem", whiteSpace: "nowrap" }}
            >
              {copied ? "Copied!" : "📋 Copy"}
            </button>
            <button
              type="button"
              className="btn btn-outline"
              onClick={() => setShowRegenerateModal(true)}
              style={{ marginBottom: "0.25rem", whiteSpace: "nowrap" }}
            >
              🔄 Regenerate
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => navigate("/system/api")}
              style={{ marginBottom: "0.25rem", whiteSpace: "nowrap" }}
            >
              📖 API Docs
            </button>
          </div>
        </div>
      </SectionCard>

      {/* Card 5: Peer IP Blocklist */}
      <SectionCard
        title="Peer IP Blocklist"
        description="Block incoming and outgoing BitTorrent peer connections from known hostile IP ranges, monitoring agencies, and corrupt peers"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}>
          {/* Live Status Summary Banner */}
          <div
            style={{
              display: "flex",
              flexWrap: "wrap",
              alignItems: "center",
              justifyContent: "space-between",
              gap: "0.75rem",
              padding: "0.75rem 1rem",
              backgroundColor: "var(--bg-primary)",
              borderRadius: "6px",
              border: "1px solid var(--border)",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
              <span
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  padding: "3px 8px",
                  borderRadius: "12px",
                  fontSize: "0.75rem",
                  fontWeight: 600,
                  backgroundColor: blocklistForm.enabled
                    ? "rgba(16, 185, 129, 0.15)"
                    : "rgba(107, 114, 128, 0.15)",
                  color: blocklistForm.enabled ? "var(--success, #10B981)" : "var(--text-muted, #9CA3AF)",
                  border: `1px solid ${blocklistForm.enabled ? "rgba(16, 185, 129, 0.3)" : "rgba(107, 114, 128, 0.3)"}`,
                }}
              >
                {blocklistForm.enabled ? "● Enforced" : "○ Disabled"}
              </span>

              <span style={{ fontSize: "0.85rem", color: "var(--text-primary)", fontWeight: 500 }}>
                {blocklistData
                  ? `${blocklistData.totalRuleCount.toLocaleString()} rules active (${blocklistData.ipv4RuleCount.toLocaleString()} IPv4 / ${blocklistData.ipv6RuleCount.toLocaleString()} IPv6)`
                  : "0 rules active"}
              </span>

              <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                | Last updated:{" "}
                {blocklistData?.lastUpdatedUtc
                  ? new Date(blocklistData.lastUpdatedUtc).toLocaleString()
                  : "Never"}
              </span>

              {blocklistData?.lastSyncStatus && (
                <span
                  style={{
                    fontSize: "0.75rem",
                    padding: "2px 6px",
                    borderRadius: "4px",
                    backgroundColor:
                      blocklistData.lastSyncStatus === "Success" || blocklistData.lastSyncStatus.includes("Not Modified")
                        ? "rgba(16, 185, 129, 0.1)"
                        : "rgba(239, 68, 68, 0.1)",
                    color:
                      blocklistData.lastSyncStatus === "Success" || blocklistData.lastSyncStatus.includes("Not Modified")
                        ? "var(--success, #10B981)"
                        : "var(--danger, #EF4444)",
                  }}
                >
                  Status: {blocklistData.lastSyncStatus}
                </span>
              )}
            </div>

            <button
              type="button"
              className="btn btn-outline"
              onClick={handleSyncBlocklist}
              disabled={syncBlocklistMutation.isPending}
              style={{ fontSize: "0.8rem", padding: "4px 12px", whiteSpace: "nowrap" }}
            >
              {syncBlocklistMutation.isPending ? "⏳ Syncing..." : "🔄 Sync Now"}
            </button>
          </div>

          {/* Form Settings */}
          <Toggle
            label="Enable Peer Blocklist"
            checked={blocklistForm.enabled}
            onChange={(v) => updateBlocklist("enabled", v)}
            hint="Filter BitTorrent peer connections against the downloaded IP blocklist ruleset"
          />

          <TextInput
            label="Blocklist Feed URL"
            value={blocklistForm.url}
            onChange={(v) => updateBlocklist("url", v)}
            placeholder="https://example.com/blocklist.gz"
            hint="Supports gzip (.gz), zip (.zip), P2P plaintext, and DAT blocklists (e.g., Bluetack, I-Blocklist, PeerGuardian format)"
          />

          <Toggle
            label="Automatic Updates"
            checked={blocklistForm.autoUpdateEnabled}
            onChange={(v) => updateBlocklist("autoUpdateEnabled", v)}
            hint="Automatically refresh blocklist rules in the background using HTTP ETag and Last-Modified caching"
          />

          {blocklistForm.autoUpdateEnabled && (
            <div style={{ display: "flex", gap: "1rem", alignItems: "flex-end", flexWrap: "wrap" }}>
              <div style={{ flex: 1, minWidth: "220px" }}>
                <SelectInput
                  label="Update Interval"
                  value={customInterval ? "custom" : String(blocklistForm.autoUpdateIntervalDays)}
                  options={[
                    { value: "1", label: "Daily (Every 24 hours)" },
                    { value: "3", label: "Every 3 days" },
                    { value: "7", label: "Weekly (Every 7 days)" },
                    { value: "14", label: "Bi-weekly (Every 14 days)" },
                    { value: "30", label: "Monthly (Every 30 days)" },
                    { value: "custom", label: "Custom interval..." },
                  ]}
                  onChange={(v) => {
                    if (v === "custom") {
                      setCustomInterval(true);
                    } else {
                      setCustomInterval(false);
                      updateBlocklist("autoUpdateIntervalDays", Number(v));
                    }
                  }}
                  hint="Frequency at which upstream blocklist changes are checked"
                />
              </div>

              {customInterval && (
                <div style={{ width: "160px" }}>
                  <NumberInput
                    label="Days"
                    value={blocklistForm.autoUpdateIntervalDays}
                    min={1}
                    max={365}
                    onChange={(v) => updateBlocklist("autoUpdateIntervalDays", v)}
                    suffix="days"
                  />
                </div>
              )}
            </div>
          )}

          {/* Save blocklist settings bar if dirty */}
          <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.5rem" }}>
            <button
              type="button"
              className={`btn ${blocklistDirty ? "btn-primary" : "btn-outline"}`}
              onClick={handleSaveBlocklist}
              disabled={!blocklistDirty || updateBlocklistMutation.isPending}
              style={{ fontSize: "0.85rem", padding: "6px 14px" }}
            >
              {updateBlocklistMutation.isPending
                ? "Saving..."
                : blocklistDirty
                  ? "💾 Save Blocklist Settings"
                  : "✓ Settings Saved"}
            </button>
          </div>

          {/* Collapsible IP Diagnostics Utility */}
          <div
            style={{
              marginTop: "0.5rem",
              borderTop: "1px solid var(--border)",
              paddingTop: "1rem",
            }}
          >
            <button
              type="button"
              onClick={() => setDiagnosticsOpen((prev) => !prev)}
              style={{
                background: "none",
                border: "none",
                color: "var(--text-primary)",
                cursor: "pointer",
                padding: 0,
                display: "flex",
                alignItems: "center",
                gap: "6px",
                fontSize: "0.9rem",
                fontWeight: 600,
              }}
            >
              <span>{diagnosticsOpen ? "▼" : "▶"}</span>
              <span>🔍 Test IP Diagnostics Utility</span>
            </button>

            {diagnosticsOpen && (
              <div
                style={{
                  marginTop: "0.75rem",
                  padding: "1rem",
                  backgroundColor: "var(--bg-primary)",
                  borderRadius: "6px",
                  border: "1px solid var(--border)",
                  display: "flex",
                  flexDirection: "column",
                  gap: "0.75rem",
                }}
              >
                <div style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}>
                  Verify whether a specific IPv4 or IPv6 peer address is blocked by the active ruleset:
                </div>
                <div style={{ display: "flex", gap: "0.5rem", alignItems: "flex-end" }}>
                  <div style={{ flex: 1 }}>
                    <TextInput
                      label="Peer IP Address"
                      value={testIpInput}
                      onChange={(v) => {
                        setTestIpInput(v);
                        setIpTestResult(null);
                      }}
                      placeholder="e.g. 198.51.100.1 or 2001:db8::1"
                    />
                  </div>
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={handleCheckIp}
                    disabled={testingIp || !testIpInput.trim()}
                    style={{ marginBottom: "0.25rem", whiteSpace: "nowrap", height: "36px" }}
                  >
                    {testingIp ? "Checking..." : "Check IP"}
                  </button>
                </div>

                {ipTestResult && (
                  <div
                    style={{
                      padding: "10px 14px",
                      borderRadius: "6px",
                      fontSize: "13px",
                      backgroundColor: ipTestResult.isBlocked
                        ? "rgba(239, 68, 68, 0.12)"
                        : "rgba(16, 185, 129, 0.12)",
                      border: `1px solid ${
                        ipTestResult.isBlocked
                          ? "rgba(239, 68, 68, 0.3)"
                          : "rgba(16, 185, 129, 0.3)"
                      }`,
                      color: ipTestResult.isBlocked
                        ? "var(--danger, #EF4444)"
                        : "var(--success, #10B981)",
                    }}
                  >
                    <div style={{ fontWeight: 600, display: "flex", alignItems: "center", gap: "6px" }}>
                      <span>{ipTestResult.isBlocked ? "🚫 BLOCKED" : "✓ ALLOWED"}</span>
                      <span>—</span>
                      <span>
                        {ipTestResult.isBlocked
                          ? "Peer connection will be dropped."
                          : "Peer is allowed to connect."}
                      </span>
                    </div>
                    {ipTestResult.rule && (
                      <div
                        style={{
                          marginTop: "4px",
                          fontSize: "0.8rem",
                          fontFamily: "monospace",
                          color: "var(--text-secondary)",
                        }}
                      >
                        Matched Rule: {ipTestResult.rule}
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </SectionCard>

      {/* Provider Add/Edit Modal */}
      {editingProvider && (
        <div
          className="modal-overlay"
          onClick={() => {
            setEditingProvider(null);
            setShowSecret(false);
          }}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: "600px",
              maxHeight: "90vh",
              overflowY: "auto",
            }}
          >
            <h2
              className="modal-title"
              style={{
                fontSize: "1.25rem",
                fontWeight: 700,
                margin: "0 0 16px 0",
              }}
            >
              {isNewProvider
                ? "Add Identity Provider"
                : `Edit Identity Provider - ${editingProvider.name || ""}`}
            </h2>

            <div
              style={{ display: "flex", flexDirection: "column", gap: "14px" }}
            >
              <Toggle
                label="Enable Provider"
                checked={editingProvider.isEnabled ?? true}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, isEnabled: v }))
                }
                hint="Allow users to log in with this provider"
              />

              <TextInput
                label="Provider Name"
                value={editingProvider.name || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, name: v }))
                }
                hint="Display name on login screen (e.g. Authentik, Keycloak, Corporate SSO)"
              />

              <TextInput
                label="Provider Identifier"
                value={editingProvider.providerId || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, providerId: v }))
                }
                hint="Unique URL-safe identifier (e.g. authentik, keycloak, google)"
              />

              <SelectInput
                label="Provider Type"
                value={String(editingProvider.providerType ?? 0)}
                options={[
                  {
                    value: "0",
                    label: "OpenID Connect (OIDC)",
                  },
                  {
                    value: "1",
                    label: "Enterprise SAML 2.0",
                  },
                  {
                    value: "2",
                    label: "Social OAuth 2.0",
                  },
                  {
                    value: "3",
                    label: "ForwardAuth (Reverse Proxy)",
                  },
                ]}
                onChange={(v) =>
                  setEditingProvider((prev) => ({
                    ...prev,
                    providerType: Number(v) as IdentityProviderType,
                  }))
                }
              />

              <TextInput
                label="Issuer URL / Authority"
                value={editingProvider.issuerUrl || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, issuerUrl: v }))
                }
                hint={
                  editingProvider.providerType === IdentityProviderType.Saml
                    ? "Identity Provider Single Sign-On Service URL (e.g. https://idp.example.com/sso/saml)"
                    : "Base URL of IdP (e.g. https://auth.example.com)"
                }
              />

              {editingProvider.providerType !== IdentityProviderType.Saml && (
                <>
                  <TextInput
                    label="Client ID"
                    value={editingProvider.clientId || ""}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({ ...prev, clientId: v }))
                    }
                  />

                  <TextInput
                    label="Client Secret"
                    type={showSecret ? "text" : "password"}
                    value={editingProvider.clientSecret || ""}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({
                        ...prev,
                        clientSecret: v,
                      }))
                    }
                    hint="Leave blank or masked to keep current secret"
                    rightElement={
                      <button
                        type="button"
                        className="btn btn-outline"
                        onClick={() => setShowSecret((prev) => !prev)}
                        style={{
                          whiteSpace: "nowrap",
                          height: "36px",
                          padding: "0 0.75rem",
                        }}
                        title={
                          showSecret
                            ? "Hide Client Secret"
                            : "Show Client Secret"
                        }
                        aria-label={
                          showSecret
                            ? "Hide Client Secret"
                            : "Show Client Secret"
                        }
                      >
                        {showSecret ? "🙈 Hide" : "👁️ Show"}
                      </button>
                    }
                  />

                  <TextInput
                    label="OAuth Scopes"
                    value={editingProvider.scopes || "openid profile email"}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({ ...prev, scopes: v }))
                    }
                    hint="Space-separated list of scopes to request (e.g. openid profile email groups)"
                  />
                </>
              )}

              {editingProvider.providerType === IdentityProviderType.Saml && (
                <>
                  <TextInput
                    label="X.509 Public Signing Certificate (PEM / Base64)"
                    value={editingProvider.certificate || ""}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({
                        ...prev,
                        certificate: v,
                      }))
                    }
                    hint="IdP public certificate used to verify SAML response digital signatures"
                  />

                  <TextInput
                    label="IdP Metadata URL (XML Endpoint)"
                    value={editingProvider.metadataUrl || ""}
                    onChange={(v) =>
                      setEditingProvider((prev) => ({
                        ...prev,
                        metadataUrl: v,
                      }))
                    }
                    hint="URL to the IdP SAML 2.0 metadata XML descriptor"
                  />
                </>
              )}

              <TextInput
                label="Role Mapping Rules (JSON)"
                value={editingProvider.roleMappingRules || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({
                    ...prev,
                    roleMappingRules: v,
                  }))
                }
                hint='JSON mapping of roles to regex patterns, e.g. {"Admin":"^(admin|admins)$"}'
              />

              <TextInput
                label="Button Text"
                value={editingProvider.buttonText || ""}
                onChange={(v) =>
                  setEditingProvider((prev) => ({ ...prev, buttonText: v }))
                }
                hint="Custom label on the login screen button (e.g. Sign in with Authentik)"
              />

              {/* Test Result Alert */}
              {testResult && (
                <div
                  style={{
                    padding: "10px 14px",
                    borderRadius: "6px",
                    fontSize: "13px",
                    backgroundColor: testResult.success
                      ? "var(--success-bg)"
                      : "var(--danger-bg-alert)",
                    border: `1px solid ${testResult.success ? "var(--success)" : "var(--danger-border-alert)"}`,
                    color: testResult.success ? "var(--success)" : "var(--danger)",
                  }}
                >
                  {testResult.success ? "✓ " : "✕ "} {testResult.message}
                </div>
              )}
            </div>

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "24px",
              }}
            >
              <button
                type="button"
                className="btn btn-outline"
                onClick={handleTestConnection}
                disabled={testing}
                style={{ fontSize: "0.85rem" }}
              >
                {testing ? "Testing Connection..." : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "8px" }}>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => {
                    setEditingProvider(null);
                    setShowSecret(false);
                  }}
                >
                  Cancel
                </button>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={handleSaveProvider}
                >
                  Save Provider
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* API Key Regeneration Confirmation Modal */}
      {showRegenerateModal && (
        <div
          className="modal-overlay"
          onClick={() => setShowRegenerateModal(false)}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{ maxWidth: "520px" }}
          >
            <h2
              className="modal-title"
              style={{
                fontSize: "1.25rem",
                fontWeight: 700,
                margin: "0 0 16px 0",
              }}
            >
              Regenerate API Key
            </h2>
            <div
              style={{
                backgroundColor: "var(--danger-bg-alert, rgba(239, 68, 68, 0.1))",
                border: "1px solid var(--danger-border-alert, rgba(239, 68, 68, 0.3))",
                color: "var(--danger, #ef4444)",
                padding: "12px 16px",
                borderRadius: "6px",
                fontSize: "0.9rem",
                lineHeight: "1.5",
                marginBottom: "20px",
              }}
            >
              Regenerating the API key will immediately invalidate the existing token and disconnect Sonarr, Radarr, Lidarr, Prowlarr, and external automation scripts. Are you sure you want to regenerate?
            </div>
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "8px",
              }}
            >
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => setShowRegenerateModal(false)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-danger"
                onClick={() => {
                  setShowRegenerateModal(false);
                  generateApiKey();
                }}
              >
                Regenerate API Key
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default SecurityTab;
