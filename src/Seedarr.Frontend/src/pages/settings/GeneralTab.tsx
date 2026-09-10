import { useState, useEffect } from "react";
import { useNavigate } from "react-router";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import { apiClient } from "../../api/client";
import { useToast } from "../../context/ToastContext";
import type { GeneralConfig } from "../../api/types";
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
  });
  const [dirty, setDirty] = useState(false);
  const [showApiKey, setShowApiKey] = useState(false);
  const [revealedApiKey, setRevealedApiKey] = useState<string | null>(null);
  const [loadingApiKey, setLoadingApiKey] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
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
