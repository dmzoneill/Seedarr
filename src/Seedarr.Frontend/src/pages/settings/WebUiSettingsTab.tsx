import { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import {
  SaveBar,
  SectionCard,
  SelectInput,
  TextInput,
  NumberInput,
  Toggle,
} from "./shared";
import { LanguageSelector } from "../../components/LanguageSelector";
import { useTheme, type Theme, type Accent } from "../../context/ThemeContext";

export function WebUiSettingsTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const saveMutation = useSaveGeneralConfig();
  const { theme: currentContextTheme, setTheme, accent: currentContextAccent, setAccent } = useTheme();

  const [form, setForm] = useState({
    themeStyle: "dark",
    colorScheme: "auto",
    port: 9898,
    bindAddress: "0.0.0.0",
    authenticationEnabled: false,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        themeStyle: config.themeStyle || currentContextTheme || "dark",
        colorScheme: config.colorScheme || currentContextAccent || "auto",
        port: config.port ?? 9898,
        bindAddress: config.bindAddress || "0.0.0.0",
        authenticationEnabled: config.authenticationEnabled ?? false,
      });
      setDirty(false);
    }
  }, [config, currentContextTheme, currentContextAccent]);

  const update = <K extends keyof typeof form>(
    key: K,
    val: (typeof form)[K],
  ) => {
    setForm((prev) => {
      const next = { ...prev, [key]: val };
      if (key === "themeStyle") {
        setTheme(val as Theme);
      }
      if (key === "colorScheme") {
        setAccent(val as Accent);
      }
      return next;
    });
    setDirty(true);
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        themeStyle: form.themeStyle,
        colorScheme: form.colorScheme,
        port: form.port,
        bindAddress: form.bindAddress,
        authenticationEnabled: form.authenticationEnabled,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading Web UI configuration...
      </div>
    );
  }

  const surfaceHexMap: Record<
    string,
    { bg: string; card: string; border: string }
  > = {
    dark: { bg: "#1a1815", card: "#2a2620", border: "#3a352e" },
    indigo: { bg: "#0b0e1e", card: "#131733", border: "#1f2552" },
    oled: { bg: "#000000", card: "#0a0b12", border: "#1a1d2e" },
    slate: { bg: "#0e131d", card: "#141c2b", border: "#1f2c42" },
    light: { bg: "#f5f0e5", card: "#fdfaf4", border: "#d4cbb8" },
    system: {
      bg: "var(--bg-primary)",
      card: "var(--bg-secondary)",
      border: "var(--border)",
    },
  };

  const accentHexMap: Record<string, string> = {
    auto: "#c8a84e",
    blue: "#3b82f6",
    emerald: "#10b981",
    purple: "#8b5cf6",
    rose: "#f43f5e",
    cyan: "#06b6d4",
    amber: "#f59e0b",
  };

  const currentSurface = surfaceHexMap[form.themeStyle] || surfaceHexMap.dark;
  const currentAccent = accentHexMap[form.colorScheme] || "#c8a84e";

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
        title="Display Language & Localization"
        description="Select user interface localization and language preferences"
      >
        <div style={{ marginBottom: "1.5rem" }}>
          <label
            style={{
              display: "block",
              marginBottom: "0.5rem",
              fontWeight: 500,
              color: "var(--text-primary)",
            }}
          >
            Interface Language
          </label>
          <div style={{ maxWidth: "280px" }}>
            <LanguageSelector />
          </div>
          <p
            style={{
              marginTop: "0.5rem",
              fontSize: "0.85rem",
              color: "var(--text-muted)",
            }}
          >
            Switch between supported languages for Seedarr UI elements
          </p>
        </div>
      </SectionCard>

      <SectionCard
        title="Appearance & Themes"
        description="Customize surface shades, contrast modes, and brand accent palettes"
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1.25rem",
            marginBottom: "1.5rem",
          }}
        >
          <SelectInput
            label="Surface Theme"
            value={form.themeStyle}
            onChange={(v) => update("themeStyle", v)}
            options={[
              { value: "dark", label: "Dark (Warm Espresso)" },
              { value: "indigo", label: "Indigo (Deep Midnight)" },
              { value: "oled", label: "OLED (Pure Black)" },
              { value: "slate", label: "Slate (Cool Steel)" },
              { value: "light", label: "Light (Cream Parchment)" },
              { value: "system", label: "System (OS Preference)" },
            ]}
            hint="Base background and surface palette for application views"
          />

          <SelectInput
            label="Accent Palette"
            value={form.colorScheme}
            onChange={(v) => update("colorScheme", v)}
            options={[
              { value: "auto", label: "Auto / Brand Gold (Default)" },
              { value: "blue", label: "Electric Blue" },
              { value: "emerald", label: "Emerald Green" },
              { value: "purple", label: "Royal Purple" },
              { value: "rose", label: "Vibrant Rose" },
              { value: "cyan", label: "Cyan Sky" },
              { value: "amber", label: "Warm Amber" },
            ]}
            hint="Primary highlight and interactive element tint color"
          />
        </div>

        {/* Live Interactive Palette Preview Card */}
        <div
          style={{
            padding: "1.25rem",
            background: currentSurface.card,
            borderRadius: "10px",
            border: `1px solid ${currentSurface.border}`,
            boxShadow: "0 4px 14px rgba(0,0,0,0.25)",
            transition: "all 0.25s ease",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              marginBottom: "1rem",
              flexWrap: "wrap",
              gap: "0.5rem",
            }}
          >
            <div
              style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
            >
              <div
                style={{
                  width: "16px",
                  height: "16px",
                  borderRadius: "50%",
                  background: currentAccent,
                  boxShadow: `0 0 10px ${currentAccent}80`,
                }}
              />
              <span
                style={{
                  fontWeight: 600,
                  fontSize: "0.95rem",
                  color: "var(--text-primary)",
                }}
              >
                Theme & Accent Live Preview
              </span>
              <span
                style={{
                  fontSize: "0.75rem",
                  padding: "0.2rem 0.5rem",
                  borderRadius: "4px",
                  background: "var(--accent-bg)",
                  color: "var(--accent)",
                  fontWeight: 600,
                }}
              >
                {form.colorScheme.toUpperCase()} ACCENT
              </span>
            </div>
            <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
              Instant live preview across all controls
            </span>
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: "1rem",
              alignItems: "center",
            }}
          >
            <div>
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-secondary)",
                  marginBottom: "0.35rem",
                }}
              >
                Action Buttons
              </div>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  type="button"
                  style={{
                    background: "var(--accent)",
                    color: "#10111a",
                    fontWeight: 700,
                    padding: "0.45rem 0.9rem",
                    borderRadius: "6px",
                    border: "none",
                    fontSize: "0.82rem",
                    cursor: "pointer",
                  }}
                >
                  Primary Action
                </button>
                <button
                  type="button"
                  style={{
                    background: "var(--accent-bg)",
                    color: "var(--accent)",
                    fontWeight: 600,
                    padding: "0.45rem 0.8rem",
                    borderRadius: "6px",
                    border: `1px solid var(--accent)`,
                    fontSize: "0.82rem",
                    cursor: "pointer",
                  }}
                >
                  Subtle Action
                </button>
              </div>
            </div>

            <div>
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-secondary)",
                  marginBottom: "0.35rem",
                }}
              >
                Progress Bar
              </div>
              <div
                style={{
                  height: "8px",
                  background: "var(--bg-primary)",
                  borderRadius: "4px",
                  overflow: "hidden",
                }}
              >
                <div
                  style={{
                    width: "72%",
                    height: "100%",
                    background: "var(--accent)",
                    borderRadius: "4px",
                    transition: "background 0.3s ease",
                  }}
                />
              </div>
            </div>

            <div>
              <div
                style={{
                  fontSize: "0.75rem",
                  color: "var(--text-secondary)",
                  marginBottom: "0.35rem",
                }}
              >
                Transfer Rate Badges
              </div>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <span
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.35rem",
                    padding: "0.3rem 0.65rem",
                    borderRadius: "6px",
                    background: "var(--accent-bg)",
                    border: "1px solid var(--accent)",
                    color: "var(--accent)",
                    fontSize: "0.8rem",
                    fontWeight: 700,
                  }}
                >
                  ↓ 42.8 MB/s
                </span>
                <span
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.35rem",
                    padding: "0.3rem 0.65rem",
                    borderRadius: "6px",
                    background: "var(--bg-primary)",
                    border: "1px solid var(--border)",
                    color: "var(--text-secondary)",
                    fontSize: "0.8rem",
                  }}
                >
                  ↑ 5.2 MB/s
                </span>
              </div>
            </div>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Web Interface Connection"
        description="Configure HTTP port and network interface bindings for the Web UI"
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
            gap: "1rem",
          }}
        >
          <NumberInput
            label="Port"
            value={form.port}
            onChange={(v) => update("port", v)}
            min={1}
            max={65535}
            hint="HTTP port to access Seedarr Web UI"
          />
          <TextInput
            label="Bind Address"
            value={form.bindAddress}
            onChange={(v) => update("bindAddress", v)}
            placeholder="0.0.0.0"
            hint="Network address interface to bind (* or 0.0.0.0 for all)"
          />
        </div>
      </SectionCard>

      <SectionCard
        title="Session Security & Authentication"
        description="Control access permissions and credential requirements"
      >
        <Toggle
          label="Authentication Enabled"
          checked={form.authenticationEnabled}
          onChange={(v) => update("authenticationEnabled", v)}
          hint="Require user login credentials for Web UI access"
        />
      </SectionCard>
    </div>
  );
}

export default WebUiSettingsTab;
