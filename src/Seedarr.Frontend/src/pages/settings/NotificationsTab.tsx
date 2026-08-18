import { useState } from "react";
import type { NotificationSettings } from "../../api/types";
import { Toggle, SelectInput, NumberInput, SectionTitle } from "./shared";

const NOTIFICATION_SETTINGS_KEY = "seedarr-notification-settings";

const defaultNotificationSettings: NotificationSettings = {
  enabled: true,
  position: "top-right",
  autoDismissSeconds: 5,
  showInfo: true,
  showSuccess: true,
  showWarning: true,
  showError: true,
};

function useNotificationSettings(): [
  NotificationSettings,
  (settings: NotificationSettings) => void,
] {
  const [settings, setSettings] = useState<NotificationSettings>(() => {
    try {
      const stored = localStorage.getItem(NOTIFICATION_SETTINGS_KEY);
      return stored
        ? { ...defaultNotificationSettings, ...JSON.parse(stored) }
        : defaultNotificationSettings;
    } catch {
      return defaultNotificationSettings;
    }
  });

  const saveSettings = (newSettings: NotificationSettings) => {
    setSettings(newSettings);
    localStorage.setItem(
      NOTIFICATION_SETTINGS_KEY,
      JSON.stringify(newSettings),
    );
  };

  return [settings, saveSettings];
}

export function NotificationsTab() {
  const [settings, saveSettings] = useNotificationSettings();
  const [form, setForm] = useState<NotificationSettings>(settings);
  const [dirty, setDirty] = useState(false);
  const [saved, setSaved] = useState(false);

  const set = <K extends keyof NotificationSettings>(
    key: K,
    value: NotificationSettings[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
    setSaved(false);
  };

  const handleSave = () => {
    saveSettings(form);
    setDirty(false);
    setSaved(true);
  };

  return (
    <div>
      <div className="settings-toolbar">
        <button
          className="btn btn-success"
          onClick={handleSave}
          disabled={!dirty}
        >
          {dirty ? "Save Changes" : "No Changes"}
        </button>
        {saved && !dirty && (
          <span
            style={{
              marginLeft: "0.75rem",
              fontSize: "0.85rem",
              color: "var(--success)",
            }}
          >
            Saved
          </span>
        )}
      </div>
      <div className="card">
        <SectionTitle>General</SectionTitle>
        <Toggle
          label="Enable Notifications"
          checked={form.enabled}
          onChange={(v) => set("enabled", v)}
          hint="Show toast notifications"
        />
        <SelectInput
          label="Position"
          value={form.position}
          onChange={(v) => set("position", v)}
          options={[
            { value: "top-right", label: "Top Right" },
            { value: "top-left", label: "Top Left" },
            { value: "bottom-right", label: "Bottom Right" },
            { value: "bottom-left", label: "Bottom Left" },
          ]}
          disabled={!form.enabled}
        />
        <NumberInput
          label="Auto-Dismiss Timeout"
          value={form.autoDismissSeconds}
          onChange={(v) => set("autoDismissSeconds", v)}
          min={1}
          max={60}
          suffix="seconds"
          disabled={!form.enabled}
        />

        <SectionTitle>Notification Types</SectionTitle>
        <Toggle
          label="Info"
          checked={form.showInfo}
          onChange={(v) => set("showInfo", v)}
          hint="General information"
        />
        <Toggle
          label="Success"
          checked={form.showSuccess}
          onChange={(v) => set("showSuccess", v)}
          hint="Successful operations"
        />
        <Toggle
          label="Warning"
          checked={form.showWarning}
          onChange={(v) => set("showWarning", v)}
          hint="Warnings and cautions"
        />
        <Toggle
          label="Error"
          checked={form.showError}
          onChange={(v) => set("showError", v)}
          hint="Errors and failures"
        />
      </div>
    </div>
  );
}
