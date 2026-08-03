import { useState, useEffect } from "react";
import { useGeneralConfig, useSaveGeneralConfig } from "../../api/hooks";
import type { GeneralConfig } from "../../api/types";
import {
  SaveBar,
  Toggle,
  SelectInput,
  TextInput,
  NumberInput,
  SectionTitle,
} from "./shared";

export function GeneralTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const save = useSaveGeneralConfig();
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

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof GeneralConfig>(
    key: K,
    value: GeneralConfig[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

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
      <div className="card">
        <SectionTitle>Application</SectionTitle>
        <Toggle
          label="Auto Start"
          checked={form.autoStart}
          onChange={(v) => set("autoStart", v)}
          hint="Start seeding on launch"
        />
        <SelectInput
          label="Theme"
          value={form.themeStyle}
          onChange={(v) => set("themeStyle", v)}
          options={[
            { value: "system", label: "System" },
            { value: "light", label: "Light" },
            { value: "dark", label: "Dark" },
          ]}
        />
        <SelectInput
          label="Color Scheme"
          value={form.colorScheme}
          onChange={(v) => set("colorScheme", v)}
          options={[
            { value: "auto", label: "Auto" },
            { value: "blue", label: "Blue" },
            { value: "green", label: "Green" },
            { value: "purple", label: "Purple" },
          ]}
        />

        <SectionTitle>Host</SectionTitle>
        <NumberInput
          label="Port"
          value={form.port}
          onChange={(v) => set("port", v)}
          min={1}
          max={65535}
        />
        <TextInput
          label="Bind Address"
          value={form.bindAddress}
          onChange={(v) => set("bindAddress", v)}
          placeholder="0.0.0.0"
        />
        <TextInput
          label="URL Base"
          value={form.urlBase}
          onChange={(v) => set("urlBase", v)}
          placeholder="/seedarr"
        />
        <Toggle
          label="Authentication"
          checked={form.authenticationEnabled}
          onChange={(v) => set("authenticationEnabled", v)}
        />
        <TextInput
          label="API Key"
          value={form.apiKey}
          onChange={(v) => set("apiKey", v)}
          hint="For external access"
        />

        <SectionTitle>Watch Folder</SectionTitle>
        <Toggle
          label="Enabled"
          checked={form.watchFolderEnabled}
          onChange={(v) => set("watchFolderEnabled", v)}
          hint="Auto-add .torrent files"
        />
        <TextInput
          label="Path"
          value={form.watchFolderPath}
          onChange={(v) => set("watchFolderPath", v)}
          placeholder="/watch"
          disabled={!form.watchFolderEnabled}
        />
        <NumberInput
          label="Scan Interval"
          value={form.watchFolderScanIntervalSeconds}
          onChange={(v) => set("watchFolderScanIntervalSeconds", v)}
          min={1}
          suffix="seconds"
          disabled={!form.watchFolderEnabled}
        />
        <Toggle
          label="Auto Start Torrents"
          checked={form.watchFolderAutoStartTorrents}
          onChange={(v) => set("watchFolderAutoStartTorrents", v)}
        />
        <Toggle
          label="Delete After Adding"
          checked={form.watchFolderDeleteAddedTorrents}
          onChange={(v) => set("watchFolderDeleteAddedTorrents", v)}
        />
      </div>
    </div>
  );
}
