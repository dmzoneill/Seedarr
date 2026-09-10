import { useState, useEffect } from "react";
import { useBitTorrentConfig, useSaveBitTorrentConfig } from "../../api/hooks";
import { SaveBar, SectionCard, TextInput, NumberInput } from "./shared";

export function CustomScriptsTab() {
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();

  const [form, setForm] = useState({
    onDownloadCompleteScript: "",
    onSeedGoalReachedScript: "",
    scriptTorrentDoneFilename: "",
    scriptTorrentAddedFilename: "",
    scriptTorrentDoneSeedingFilename: "",
    customScriptTimeoutSeconds: 60,
  });

  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm({
        onDownloadCompleteScript: config.onDownloadCompleteScript || "",
        onSeedGoalReachedScript: config.onSeedGoalReachedScript || "",
        scriptTorrentDoneFilename: config.scriptTorrentDoneFilename || "",
        scriptTorrentAddedFilename: config.scriptTorrentAddedFilename || "",
        scriptTorrentDoneSeedingFilename:
          config.scriptTorrentDoneSeedingFilename || "",
        customScriptTimeoutSeconds: config.customScriptTimeoutSeconds || 60,
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
  };

  const handleSave = () => {
    if (!config) return;
    saveMutation.mutate(
      {
        ...config,
        onDownloadCompleteScript: form.onDownloadCompleteScript,
        onSeedGoalReachedScript: form.onSeedGoalReachedScript,
        scriptTorrentDoneFilename: form.scriptTorrentDoneFilename,
        scriptTorrentAddedFilename: form.scriptTorrentAddedFilename,
        scriptTorrentDoneSeedingFilename: form.scriptTorrentDoneSeedingFilename,
        customScriptTimeoutSeconds: form.customScriptTimeoutSeconds,
      },
      {
        onSuccess: () => setDirty(false),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading custom scripts configuration...
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

      <SectionCard
        title="Lifecycle Event Scripts"
        description="Execute custom shell scripts or executables (.sh, .py, .bat, .ps1) when torrent lifecycle events occur"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <TextInput
            label="On Download Complete Script"
            value={form.onDownloadCompleteScript}
            onChange={(v) => update("onDownloadCompleteScript", v)}
            placeholder="/usr/local/bin/on-download-complete.sh"
            hint="Absolute path to script executed when a torrent finishes downloading and enters seeding"
          />

          <TextInput
            label="On Seed Goal Reached Script"
            value={form.onSeedGoalReachedScript}
            onChange={(v) => update("onSeedGoalReachedScript", v)}
            placeholder="/usr/local/bin/on-seed-goal.py"
            hint="Absolute path to script executed when a torrent reaches its target ratio or seeding time"
          />

          <NumberInput
            label="Script Execution Timeout"
            value={form.customScriptTimeoutSeconds}
            onChange={(v) => update("customScriptTimeoutSeconds", v)}
            min={5}
            max={3600}
            suffix="seconds"
            hint="Maximum duration a script is allowed to run before being automatically terminated (default: 60s)"
          />

          <div
            style={{
              backgroundColor: "var(--bg-primary, #10111a)",
              padding: "1rem",
              borderRadius: "6px",
              border: "1px solid var(--border-light, #1c203b)",
            }}
          >
            <div
              style={{
                fontSize: "0.85rem",
                fontWeight: 600,
                color: "var(--text-secondary, #a2a6b8)",
                marginBottom: "0.5rem",
              }}
            >
              Environment Variables Exported to Scripts
            </div>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted, #7e8092)",
                fontFamily: "monospace",
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
                gap: "0.35rem",
              }}
            >
              <div>• SEEDARR_TORRENT_ID</div>
              <div>• SEEDARR_TORRENT_NAME</div>
              <div>• SEEDARR_TORRENT_PATH</div>
              <div>• SEEDARR_TORRENT_CATEGORY</div>
              <div>• SEEDARR_TORRENT_INFOHASH</div>
              <div>• SEEDARR_TORRENT_SIZE_BYTES</div>
              <div>• SEEDARR_TORRENT_RATIO</div>
              <div>• SEEDARR_EVENT_TYPE</div>
            </div>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Transmission-Compatible Script Hooks"
        description="Compatibility script hooks triggered in the style of Transmission daemon"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          <TextInput
            label="Script Torrent Done Filename"
            value={form.scriptTorrentDoneFilename}
            onChange={(v) => update("scriptTorrentDoneFilename", v)}
            placeholder="/usr/local/bin/transmission-done.sh"
            hint="Script run when torrent finishes downloading (TR_TORRENT_DIR, TR_TORRENT_NAME exported)"
          />

          <TextInput
            label="Script Torrent Added Filename"
            value={form.scriptTorrentAddedFilename}
            onChange={(v) => update("scriptTorrentAddedFilename", v)}
            placeholder="/usr/local/bin/transmission-added.sh"
            hint="Script run when a new torrent is added"
          />

          <TextInput
            label="Script Torrent Done Seeding Filename"
            value={form.scriptTorrentDoneSeedingFilename}
            onChange={(v) => update("scriptTorrentDoneSeedingFilename", v)}
            placeholder="/usr/local/bin/transmission-done-seeding.sh"
            hint="Script run when torrent meets seeding goal or finishes seeding"
          />
        </div>
      </SectionCard>
    </div>
  );
}

export default CustomScriptsTab;
