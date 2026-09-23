import { useState, useEffect } from "react";
import {
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  useTestCustomScript,
} from "../../api/hooks";
import type { CustomScriptTestResult } from "../../api/types";
import { SaveBar, SectionCard, TextInput, NumberInput } from "./shared";
import { useFocusTrap } from "../../hooks/useFocusTrap";
import { useModalRegistration } from "../../components/ModalProvider";

interface TestModalState {
  title: string;
  scriptPath: string;
  result: CustomScriptTestResult;
}

function getActionableTip(result: CustomScriptTestResult): string | null {
  if (result.timedOut) {
    return "Script execution timed out before completion. Consider increasing the Script Execution Timeout setting or optimizing script performance.";
  }
  if (result.exitCode === 126) {
    return "Permission denied. Check script execute permissions (run `chmod +x <script>` to make it executable).";
  }
  if (result.exitCode === 127) {
    return "Command or interpreter not found. Ensure the script interpreter (e.g., /bin/sh, python3, pwsh, node) is installed and available in PATH.";
  }
  if (result.exitCode === 128) {
    return "Invalid argument to exit command or script terminated prematurely.";
  }
  if (result.exitCode > 128 && result.exitCode <= 143) {
    return `Script was terminated by fatal signal ${result.exitCode - 128} (e.g. SIGTERM/SIGKILL).`;
  }
  if (
    result.stderr.toLowerCase().includes("does not exist") ||
    result.stderr.toLowerCase().includes("no such file")
  ) {
    return "Script file does not exist. Verify the script path exists on the host system and is accessible to Seedarr.";
  }
  if (result.stderr.toLowerCase().includes("permission denied")) {
    return "Permission denied accessing or executing script file. Verify file permissions and ownership (`chmod +x`).";
  }
  if (!result.success && result.exitCode !== 0) {
    return `Script returned non-zero exit code ${result.exitCode}. Review the stderr output above for details.`;
  }
  if (result.success) {
    return "Script executed successfully with exit code 0.";
  }
  return null;
}

export function CustomScriptsTab() {
  const { data: config, isLoading } = useBitTorrentConfig();
  const saveMutation = useSaveBitTorrentConfig();
  const testMutation = useTestCustomScript();

  const [form, setForm] = useState({
    onDownloadCompleteScript: "",
    onSeedGoalReachedScript: "",
    scriptTorrentDoneFilename: "",
    scriptTorrentAddedFilename: "",
    scriptTorrentDoneSeedingFilename: "",
    customScriptTimeoutSeconds: 60,
  });

  const [dirty, setDirty] = useState(false);
  const [activeTestingKey, setActiveTestingKey] = useState<string | null>(null);
  const [testModalData, setTestModalData] = useState<TestModalState | null>(
    null,
  );

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

  const testModalTrapRef = useFocusTrap<HTMLDivElement>({
    isOpen: Boolean(testModalData),
    onEscape: () => setTestModalData(null),
  });
  useModalRegistration({
    id: "custom-scripts-test-modal",
    isOpen: Boolean(testModalData),
    onClose: () => setTestModalData(null),
    modalRef: testModalTrapRef,
  });

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

  const handleTestScript = (
    scriptPath: string,
    eventType: string,
    scriptTitle: string,
    key: string,
  ) => {
    if (!scriptPath || !scriptPath.trim()) return;
    setActiveTestingKey(key);
    testMutation.mutate(
      { scriptPath: scriptPath.trim(), eventType },
      {
        onSuccess: (data) => {
          setTestModalData({
            title: scriptTitle,
            scriptPath: scriptPath.trim(),
            result: data,
          });
          setActiveTestingKey(null);
        },
        onError: (err) => {
          setTestModalData({
            title: scriptTitle,
            scriptPath: scriptPath.trim(),
            result: {
              success: false,
              exitCode: -1,
              stdout: "",
              stderr: err.message || "Failed to communicate with test endpoint",
              executionTimeMs: 0,
              timedOut: false,
            },
          });
          setActiveTestingKey(null);
        },
      },
    );
  };

  const renderTestButton = (
    key: string,
    scriptPath: string,
    eventType: string,
    scriptTitle: string,
  ) => {
    const isTesting = activeTestingKey === key && testMutation.isPending;
    return (
      <button
        type="button"
        className="btn btn-outline btn-small"
        disabled={!scriptPath || !scriptPath.trim() || testMutation.isPending}
        onClick={() =>
          handleTestScript(scriptPath, eventType, scriptTitle, key)
        }
        style={{ whiteSpace: "nowrap" }}
        title={
          !scriptPath || !scriptPath.trim()
            ? "Enter a script path to test"
            : `Test ${scriptTitle}`
        }
      >
        {isTesting ? "Testing..." : "Test"}
      </button>
    );
  };

  if (isLoading) {
    return (
      <div className="loading" style={{ padding: "2rem" }}>
        Loading custom scripts configuration...
      </div>
    );
  }

  const tip = testModalData ? getActionableTip(testModalData.result) : null;

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
            rightElement={renderTestButton(
              "onDownloadCompleteScript",
              form.onDownloadCompleteScript,
              "OnDownloadComplete",
              "On Download Complete Script",
            )}
          />

          <TextInput
            label="On Seed Goal Reached Script"
            value={form.onSeedGoalReachedScript}
            onChange={(v) => update("onSeedGoalReachedScript", v)}
            placeholder="/usr/local/bin/on-seed-goal.py"
            hint="Absolute path to script executed when a torrent reaches its target ratio or seeding time"
            rightElement={renderTestButton(
              "onSeedGoalReachedScript",
              form.onSeedGoalReachedScript,
              "OnSeedGoalReached",
              "On Seed Goal Reached Script",
            )}
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
              border: "1px solid var(--border-light)",
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
            rightElement={renderTestButton(
              "scriptTorrentDoneFilename",
              form.scriptTorrentDoneFilename,
              "TorrentDone",
              "Script Torrent Done Filename",
            )}
          />

          <TextInput
            label="Script Torrent Added Filename"
            value={form.scriptTorrentAddedFilename}
            onChange={(v) => update("scriptTorrentAddedFilename", v)}
            placeholder="/usr/local/bin/transmission-added.sh"
            hint="Script run when a new torrent is added"
            rightElement={renderTestButton(
              "scriptTorrentAddedFilename",
              form.scriptTorrentAddedFilename,
              "TorrentAdded",
              "Script Torrent Added Filename",
            )}
          />

          <TextInput
            label="Script Torrent Done Seeding Filename"
            value={form.scriptTorrentDoneSeedingFilename}
            onChange={(v) => update("scriptTorrentDoneSeedingFilename", v)}
            placeholder="/usr/local/bin/transmission-done-seeding.sh"
            hint="Script run when torrent meets seeding goal or finishes seeding"
            rightElement={renderTestButton(
              "scriptTorrentDoneSeedingFilename",
              form.scriptTorrentDoneSeedingFilename,
              "TorrentDoneSeeding",
              "Script Torrent Done Seeding Filename",
            )}
          />
        </div>
      </SectionCard>

      {testModalData && (
        <div
          className="modal-overlay"
          onClick={() => setTestModalData(null)}
          role="dialog"
          aria-modal="true"
          aria-labelledby="script-test-modal-title"
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: "rgba(0, 0, 0, 0.75)",
            backdropFilter: "blur(6px)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 9999,
            padding: "1rem",
          }}
        >
          <div
            ref={testModalTrapRef}
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              width: "100%",
              maxWidth: 640,
              backgroundColor: "var(--bg-card, #161826)",
              borderRadius: "8px",
              padding: "1.5rem",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
              maxHeight: "90vh",
              overflowY: "auto",
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "flex-start",
                marginBottom: "1rem",
              }}
            >
              <div>
                <h3
                  id="script-test-modal-title"
                  style={{
                    margin: 0,
                    fontSize: "1.2rem",
                    fontWeight: 600,
                    color: "var(--text-primary, #fff)",
                  }}
                >
                  Script Test Results
                </h3>
                <div
                  style={{
                    fontSize: "0.82rem",
                    color: "var(--text-muted, #94a3b8)",
                    marginTop: "0.25rem",
                  }}
                >
                  {testModalData.title} &bull;{" "}
                  <code style={{ fontSize: "0.8rem" }}>
                    {testModalData.scriptPath}
                  </code>
                </div>
              </div>
              <button
                type="button"
                onClick={() => setTestModalData(null)}
                style={{
                  background: "transparent",
                  border: "none",
                  color: "var(--text-muted, #94a3b8)",
                  fontSize: "1.25rem",
                  cursor: "pointer",
                  padding: "0.25rem",
                  lineHeight: 1,
                }}
                aria-label="Close"
              >
                &times;
              </button>
            </div>

            {/* Badges & Stats */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.75rem",
                flexWrap: "wrap",
                marginBottom: "1rem",
                padding: "0.75rem",
                backgroundColor: "var(--bg-primary, #10111a)",
                borderRadius: "6px",
                border: "1px solid var(--border-light)",
              }}
            >
              <span
                style={{
                  padding: "0.25rem 0.6rem",
                  borderRadius: "4px",
                  fontSize: "0.82rem",
                  fontWeight: 600,
                  backgroundColor: testModalData.result.success
                    ? "rgba(16, 185, 129, 0.15)"
                    : "rgba(239, 68, 68, 0.15)",
                  color: testModalData.result.success ? "#10b981" : "#ef4444",
                  border: `1px solid ${
                    testModalData.result.success
                      ? "rgba(16, 185, 129, 0.3)"
                      : "rgba(239, 68, 68, 0.3)"
                  }`,
                }}
              >
                {testModalData.result.success
                  ? `✓ Success (Exit Code: ${testModalData.result.exitCode})`
                  : testModalData.result.timedOut
                    ? `⏱ Timed Out (Exit Code: ${testModalData.result.exitCode})`
                    : `✕ Failed (Exit Code: ${testModalData.result.exitCode})`}
              </span>

              <span
                style={{
                  fontSize: "0.82rem",
                  color: "var(--text-secondary, #a2a6b8)",
                }}
              >
                Duration:{" "}
                <strong style={{ color: "var(--text-primary, #fff)" }}>
                  {testModalData.result.executionTimeMs} ms
                </strong>
              </span>

              {testModalData.result.resolvedInterpreter && (
                <span
                  style={{
                    fontSize: "0.82rem",
                    color: "var(--text-secondary, #a2a6b8)",
                  }}
                >
                  Interpreter:{" "}
                  <code style={{ fontSize: "0.8rem" }}>
                    {testModalData.result.resolvedInterpreter}
                  </code>
                </span>
              )}

              {testModalData.result.workingDirectory && (
                <span
                  style={{
                    fontSize: "0.82rem",
                    color: "var(--text-secondary, #a2a6b8)",
                  }}
                >
                  Working Dir:{" "}
                  <code style={{ fontSize: "0.8rem" }}>
                    {testModalData.result.workingDirectory}
                  </code>
                </span>
              )}
            </div>

            {/* Actionable Tips */}
            {tip && (
              <div
                style={{
                  marginBottom: "1rem",
                  padding: "0.75rem 1rem",
                  backgroundColor: testModalData.result.success
                    ? "rgba(16, 185, 129, 0.08)"
                    : "rgba(234, 179, 8, 0.08)",
                  border: `1px solid ${
                    testModalData.result.success
                      ? "rgba(16, 185, 129, 0.3)"
                      : "rgba(234, 179, 8, 0.3)"
                  }`,
                  borderRadius: "6px",
                  fontSize: "0.85rem",
                  color: testModalData.result.success ? "#10b981" : "#facc15",
                  display: "flex",
                  alignItems: "flex-start",
                  gap: "0.5rem",
                }}
              >
                <span style={{ fontSize: "1rem", lineHeight: 1 }}>💡</span>
                <div>
                  <strong>Tip: </strong>
                  {tip}
                </div>
              </div>
            )}

            {/* Standard Output */}
            <div style={{ marginBottom: "1rem" }}>
              <div
                style={{
                  fontSize: "0.82rem",
                  fontWeight: 600,
                  color: "var(--text-secondary, #a2a6b8)",
                  marginBottom: "0.35rem",
                }}
              >
                Standard Output (stdout)
              </div>
              {testModalData.result.stdout ? (
                <pre
                  style={{
                    margin: 0,
                    padding: "0.75rem",
                    backgroundColor: "var(--bg-primary, #0d0f17)",
                    borderRadius: "6px",
                    border: "1px solid var(--border-light, #2e344e)",
                    fontFamily: "monospace",
                    fontSize: "0.82rem",
                    lineHeight: 1.4,
                    color: "#a7f3d0",
                    maxHeight: "180px",
                    overflowY: "auto",
                    whiteSpace: "pre-wrap",
                    wordBreak: "break-all",
                  }}
                >
                  {testModalData.result.stdout}
                </pre>
              ) : (
                <div
                  style={{
                    fontSize: "0.82rem",
                    color: "var(--text-muted)",
                    fontStyle: "italic",
                  }}
                >
                  (No standard output produced)
                </div>
              )}
            </div>

            {/* Standard Error */}
            <div style={{ marginBottom: "1.25rem" }}>
              <div
                style={{
                  fontSize: "0.82rem",
                  fontWeight: 600,
                  color: "var(--text-secondary, #a2a6b8)",
                  marginBottom: "0.35rem",
                }}
              >
                Standard Error (stderr)
              </div>
              {testModalData.result.stderr ? (
                <pre
                  style={{
                    margin: 0,
                    padding: "0.75rem",
                    backgroundColor: "rgba(239, 68, 68, 0.08)",
                    borderRadius: "6px",
                    border: "1px solid rgba(239, 68, 68, 0.3)",
                    fontFamily: "monospace",
                    fontSize: "0.82rem",
                    lineHeight: 1.4,
                    color: "#fca5a5",
                    maxHeight: "180px",
                    overflowY: "auto",
                    whiteSpace: "pre-wrap",
                    wordBreak: "break-all",
                  }}
                >
                  {testModalData.result.stderr}
                </pre>
              ) : (
                <div
                  style={{
                    fontSize: "0.82rem",
                    color: "var(--text-muted)",
                    fontStyle: "italic",
                  }}
                >
                  (No standard error produced)
                </div>
              )}
            </div>

            {/* Modal Actions */}
            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setTestModalData(null)}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default CustomScriptsTab;
