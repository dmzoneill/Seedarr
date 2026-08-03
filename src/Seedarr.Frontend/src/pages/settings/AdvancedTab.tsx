import { useState, useEffect } from "react";
import { useAdvancedConfig, useSaveAdvancedConfig } from "../../api/hooks";
import type { AdvancedConfig } from "../../api/types";
import {
  SaveBar,
  Toggle,
  SelectInput,
  NumberInput,
  SectionTitle,
} from "./shared";

export function AdvancedTab() {
  const { data: config, isLoading } = useAdvancedConfig();
  const save = useSaveAdvancedConfig();
  const [form, setForm] = useState<AdvancedConfig>({
    id: 1,
    logToFile: true,
    fileLogLevel: "Info",
    debugMode: false,
    uiRefreshRateSec: 9,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof AdvancedConfig>(
    key: K,
    value: AdvancedConfig[K],
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
        <SectionTitle>Logging</SectionTitle>
        <Toggle
          label="Log to File"
          checked={form.logToFile}
          onChange={(v) => set("logToFile", v)}
        />
        <SelectInput
          label="Log Level"
          value={form.fileLogLevel}
          onChange={(v) => set("fileLogLevel", v)}
          options={[
            { value: "Trace", label: "Trace" },
            { value: "Debug", label: "Debug" },
            { value: "Info", label: "Info" },
            { value: "Warn", label: "Warn" },
            { value: "Error", label: "Error" },
          ]}
          disabled={!form.logToFile}
        />
        <Toggle
          label="Debug Mode"
          checked={form.debugMode}
          onChange={(v) => set("debugMode", v)}
        />

        <SectionTitle>UI</SectionTitle>
        <NumberInput
          label="Refresh Rate"
          value={form.uiRefreshRateSec}
          onChange={(v) => set("uiRefreshRateSec", v)}
          min={1}
          max={60}
          suffix="seconds"
        />
      </div>
    </div>
  );
}
