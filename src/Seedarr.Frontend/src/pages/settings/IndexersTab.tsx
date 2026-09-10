import { useState } from "react";
import {
  useIndexers,
  useCreateIndexer,
  useUpdateIndexer,
  useDeleteIndexer,
  useTestIndexer,
  useTestDirectIndexer,
  useRssRules,
  useCreateRssRule,
  useUpdateRssRule,
  useDeleteRssRule,
  useSyncRss,
} from "../../api/hooks";
import type { IndexerDefinition, IndexerTestResult, RssRule } from "../../api/types";
import { TextInput, SelectInput, Toggle, NumberInput, SectionCard } from "./shared";
import { useToast } from "../../context/ToastContext";

export function IndexersTab() {
  const { showToast } = useToast();

  // Indexers
  const { data: indexers, isLoading } = useIndexers();
  const createMutation = useCreateIndexer();
  const updateMutation = useUpdateIndexer();
  const deleteMutation = useDeleteIndexer();
  const testMutation = useTestIndexer();
  const testDirectMutation = useTestDirectIndexer();
  const [editing, setEditing] = useState<Partial<IndexerDefinition> | null>(null);
  const [testResults, setTestResults] = useState<Record<number, boolean | null>>({});
  const [modalTestResult, setModalTestResult] = useState<IndexerTestResult | null>(null);

  // RSS Rules
  const { data: rssRules, isLoading: isRssRulesLoading } = useRssRules();
  const createRuleMutation = useCreateRssRule();
  const updateRuleMutation = useUpdateRssRule();
  const deleteRuleMutation = useDeleteRssRule();
  const syncRssMutation = useSyncRss();
  const [editingRule, setEditingRule] = useState<Partial<RssRule> | null>(null);

  const defaultIndexer: Partial<IndexerDefinition> = {
    name: "Prowlarr",
    indexerType: "Prowlarr",
    url: "http://prowlarr:9696",
    apiKey: "",
    apiPath: "/api",
    enableRss: true,
    enableSearch: true,
    categories: "",
    downloadClientId: 0,
    enable: true,
  };

  const defaultRssRule: Partial<RssRule> = {
    name: "New RSS Rule",
    isEnabled: true,
    mustContain: "",
    mustNotContain: "",
    minSeeders: 1,
    minSizeBytes: 0,
    maxSizeBytes: 0,
    maxAgeDays: 0,
    freeleechOnly: false,
    categoryId: 0,
    indexerIds: [],
  };

  const handleSave = () => {
    if (!editing) return;
    const name = editing.name?.trim() || editing.indexerType || "Indexer";
    const payload = {
      ...editing,
      name,
      enable: editing.enable ?? true,
      implementation: `${editing.indexerType || "Prowlarr"}Indexer`,
      configContract: "IndexerDefinition",
    };
    if (editing.id) {
      updateMutation.mutate(payload as IndexerDefinition, {
        onSuccess: () => {
          setEditing(null);
          setModalTestResult(null);
          showToast(`Indexer "${payload.name}" updated`, "success");
        },
        onError: (err: any) => {
          showToast(err?.message || "Failed to update indexer", "error");
        },
      });
    } else {
      createMutation.mutate(payload, {
        onSuccess: () => {
          setEditing(null);
          setModalTestResult(null);
          showToast(`Indexer "${payload.name}" created`, "success");
        },
        onError: (err: any) => {
          showToast(err?.message || "Failed to create indexer", "error");
        },
      });
    }
  };

  const handleTest = (id: number) => {
    setTestResults((prev) => ({ ...prev, [id]: null }));
    testMutation.mutate(id, {
      onSuccess: (data) => {
        setTestResults((prev) => ({ ...prev, [id]: data.success }));
        if (data.success) {
          showToast("Indexer connection successful", "success");
        } else {
          showToast(`Indexer connection failed: ${data.message || "Unknown error"}`, "error");
        }
      },
      onError: () => {
        setTestResults((prev) => ({ ...prev, [id]: false }));
        showToast("Indexer connection test failed", "error");
      },
    });
  };

  const handleModalTest = () => {
    if (!editing) return;
    setModalTestResult(null);
    testDirectMutation.mutate(editing, {
      onSuccess: (res) => {
        setModalTestResult(res);
        if (res.success) {
          showToast("Indexer connection successful", "success");
        } else {
          showToast(`Indexer connection failed: ${res.message || "Unknown error"}`, "error");
        }
      },
      onError: (err) => {
        setModalTestResult({
          success: false,
          message: err.message || "Failed to test indexer connection.",
        });
        showToast(`Indexer connection failed: ${err.message || "Unknown error"}`, "error");
      },
    });
  };

  const handleSaveRule = () => {
    if (!editingRule) return;
    const name = editingRule.name?.trim() || "RSS Rule";
    const payload: Partial<RssRule> = {
      ...editingRule,
      name,
      isEnabled: editingRule.isEnabled ?? true,
      mustContain: editingRule.mustContain?.trim() || "",
      mustNotContain: editingRule.mustNotContain?.trim() || "",
      minSeeders: Number(editingRule.minSeeders) || 0,
      minSizeBytes: Number(editingRule.minSizeBytes) || 0,
      maxSizeBytes: Number(editingRule.maxSizeBytes) || 0,
      maxAgeDays: Number(editingRule.maxAgeDays) || 0,
      freeleechOnly: Boolean(editingRule.freeleechOnly),
      categoryId: Number(editingRule.categoryId) || 0,
      indexerIds: editingRule.indexerIds || [],
    };

    if (editingRule.id) {
      updateRuleMutation.mutate(payload as RssRule, {
        onSuccess: () => {
          setEditingRule(null);
          showToast(`RSS Rule "${name}" updated`, "success");
        },
        onError: (err: any) => {
          showToast(err?.message || "Failed to update RSS rule", "error");
        },
      });
    } else {
      createRuleMutation.mutate(payload, {
        onSuccess: () => {
          setEditingRule(null);
          showToast(`RSS Rule "${name}" created`, "success");
        },
        onError: (err: any) => {
          showToast(err?.message || "Failed to create RSS rule", "error");
        },
      });
    }
  };

  const handleSyncRssNow = () => {
    syncRssMutation.mutate(undefined, {
      onSuccess: (res) => {
        showToast(`RSS sync completed successfully (${res.grabbedCount} releases grabbed)`, "success");
      },
      onError: (err: any) => {
        showToast(err?.message || "RSS sync failed", "error");
      },
    });
  };

  if (isLoading || isRssRulesLoading) return <div className="loading">Loading indexers...</div>;

  return (
    <>
      <SectionCard
        title="Torznab & Newznab Indexers"
        description="Configure Prowlarr, Jackett, or standalone Torznab/Newznab indexers for automated releases"
      >
        <div className="provider-cards">
          {indexers?.map((idx) => (
            <div
              key={idx.id}
              className="provider-card"
              onClick={() => {
                setEditing({ ...idx });
                setModalTestResult(null);
              }}
            >
              <div className="provider-card-actions">
                {idx.url && (
                  <a
                    href={idx.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="provider-card-action"
                    title={`Open ${idx.url}`}
                    onClick={(e) => e.stopPropagation()}
                    style={{ textDecoration: "none", color: "inherit" }}
                  >
                    ↗
                  </a>
                )}
                <button
                  className="provider-card-action"
                  title="Test Connection"
                  onClick={(e) => {
                    e.stopPropagation();
                    handleTest(idx.id);
                  }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete Indexer"
                  onClick={(e) => {
                    e.stopPropagation();
                    if (window.confirm(`Are you sure you want to delete indexer "${idx.name}"?`)) {
                      deleteMutation.mutate(idx.id, {
                        onSuccess: () => showToast(`Indexer "${idx.name}" deleted`, "info"),
                      });
                    }
                  }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{idx.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">
                  {idx.indexerType}
                </span>
                {idx.enable === false && (
                  <span className="provider-card-badge provider-card-badge-gray">
                    Disabled
                  </span>
                )}
                {idx.enableRss && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    RSS
                  </span>
                )}
                {idx.enableSearch && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    Search
                  </span>
                )}
              </div>
              <div className="provider-card-info">{idx.url}</div>
              {testResults[idx.id] === true && (
                <div className="provider-card-test provider-card-test-ok">
                  Connection Passed
                </div>
              )}
              {testResults[idx.id] === false && (
                <div className="provider-card-test provider-card-test-fail">
                  Connection Failed
                </div>
              )}
              {testResults[idx.id] === null && (
                <div className="provider-card-test provider-card-test-pending">
                  Testing...
                </div>
              )}
            </div>
          ))}
          <div
            className="provider-card-add"
            onClick={() => {
              setEditing({ ...defaultIndexer });
              setModalTestResult(null);
            }}
            title="Add Indexer"
          >
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="RSS Automation Rules"
        description="Automate release filtering with regex criteria, seeders, size thresholds, and freeleech filters"
      >
        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            marginBottom: "1rem",
          }}
        >
          <button
            type="button"
            className="btn btn-outline btn-small"
            onClick={handleSyncRssNow}
            disabled={syncRssMutation.isPending}
          >
            {syncRssMutation.isPending ? "Syncing RSS..." : "🔄 Sync RSS Now"}
          </button>
        </div>

        <div className="provider-cards">
          {rssRules?.map((rule) => (
            <div
              key={rule.id}
              className="provider-card"
              onClick={() => setEditingRule({ ...rule })}
            >
              <div className="provider-card-actions">
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete RSS Rule"
                  onClick={(e) => {
                    e.stopPropagation();
                    if (window.confirm(`Are you sure you want to delete the RSS rule "${rule.name}"?`)) {
                      deleteRuleMutation.mutate(rule.id, {
                        onSuccess: () => showToast(`RSS Rule "${rule.name}" deleted`, "info"),
                        onError: (err: any) => showToast(err?.message || "Failed to delete RSS rule", "error"),
                      });
                    }
                  }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{rule.name}</div>
              <div className="provider-card-badges">
                <span
                  className={`provider-card-badge ${
                    rule.isEnabled
                      ? "provider-card-badge-green"
                      : "provider-card-badge-gray"
                  }`}
                >
                  {rule.isEnabled ? "Enabled" : "Disabled"}
                </span>
                {rule.minSeeders > 0 && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    ≥ {rule.minSeeders} seeds
                  </span>
                )}
                {rule.freeleechOnly && (
                  <span className="provider-card-badge provider-card-badge-gold">
                    Freeleech
                  </span>
                )}
                {rule.maxAgeDays != null && rule.maxAgeDays > 0 && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    ≤ {rule.maxAgeDays}d
                  </span>
                )}
                {rule.categoryId > 0 && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    Cat: {rule.categoryId}
                  </span>
                )}
                <span className="provider-card-badge provider-card-badge-blue">
                  {rule.indexerIds && rule.indexerIds.length > 0
                    ? `${rule.indexerIds.length} Indexers`
                    : "All Indexers"}
                </span>
              </div>
              <div className="provider-card-info">
                {rule.mustContain && (
                  <div style={{ wordBreak: "break-all" }}>
                    <strong>Must Contain:</strong> <code>{rule.mustContain}</code>
                  </div>
                )}
                {rule.mustNotContain && (
                  <div style={{ wordBreak: "break-all" }}>
                    <strong>Must Not Contain:</strong> <code>{rule.mustNotContain}</code>
                  </div>
                )}
                {!rule.mustContain && !rule.mustNotContain && (
                  <div>Catch-all rule (matches all releases)</div>
                )}
              </div>
            </div>
          ))}
          <div
            className="provider-card-add"
            onClick={() => setEditingRule({ ...defaultRssRule })}
            title="Add RSS Rule"
          >
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </SectionCard>

      {editing && (
        <div
          className="modal-overlay"
          onClick={() => {
            setEditing(null);
            setModalTestResult(null);
          }}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 520,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid rgba(255, 255, 255, 0.12)",
            }}
          >
            <div
              className="modal-title"
              style={{ fontSize: "1.2rem", marginBottom: "1rem" }}
            >
              {editing.id ? "Edit Indexer" : "Add Indexer"}
            </div>
            <TextInput
              label="Name"
              value={editing.name || ""}
              onChange={(v) => {
                setEditing({ ...editing, name: v });
                setModalTestResult(null);
              }}
              placeholder="e.g. My Prowlarr"
            />
            <SelectInput
              label="Indexer Type"
              value={editing.indexerType || "Prowlarr"}
              onChange={(v) => {
                const defaults: Record<string, string> = {
                  Prowlarr: "http://localhost:9696",
                  Torznab: "http://localhost:9117",
                  Newznab: "http://localhost:5076",
                };
                setEditing({
                  ...editing,
                  indexerType: v,
                  url: defaults[v] || editing.url || "",
                });
                setModalTestResult(null);
              }}
              options={[
                { value: "Prowlarr", label: "Prowlarr" },
                { value: "Torznab", label: "Torznab (Jackett / generic)" },
                { value: "Newznab", label: "Newznab (NZBGeek / generic)" },
              ]}
            />
            <TextInput
              label="URL"
              value={editing.url || ""}
              onChange={(v) => {
                setEditing({ ...editing, url: v });
                setModalTestResult(null);
              }}
              placeholder="http://localhost:9696"
            />
            <TextInput
              label="API Key"
              value={editing.apiKey || ""}
              onChange={(v) => {
                setEditing({ ...editing, apiKey: v });
                setModalTestResult(null);
              }}
              type="password"
            />
            <TextInput
              label="API Path"
              value={editing.apiPath || "/api"}
              onChange={(v) => {
                setEditing({ ...editing, apiPath: v });
                setModalTestResult(null);
              }}
              placeholder="api"
            />
            <TextInput
              label="Categories"
              value={
                Array.isArray(editing.categories)
                  ? editing.categories.join(",")
                  : editing.categories || ""
              }
              onChange={(v) => {
                setEditing({ ...editing, categories: v });
                setModalTestResult(null);
              }}
              placeholder="2000,5000"
            />
            <Toggle
              label="Enable Indexer"
              checked={editing.enable ?? true}
              onChange={(v) => {
                setEditing({ ...editing, enable: v });
                setModalTestResult(null);
              }}
            />
            <Toggle
              label="Enable RSS"
              checked={editing.enableRss ?? true}
              onChange={(v) => {
                setEditing({ ...editing, enableRss: v });
                setModalTestResult(null);
              }}
            />
            <Toggle
              label="Enable Search"
              checked={editing.enableSearch ?? true}
              onChange={(v) => {
                setEditing({ ...editing, enableSearch: v });
                setModalTestResult(null);
              }}
            />

            {testDirectMutation.isPending && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  fontSize: "0.875rem",
                  backgroundColor: "rgba(200, 168, 78, 0.12)",
                  color: "var(--accent, #c8a84e)",
                  border: "1px solid rgba(200, 168, 78, 0.35)",
                  display: "flex",
                  alignItems: "center",
                  gap: "0.5rem",
                }}
              >
                <span>Testing connection to {editing.url || "indexer"}...</span>
              </div>
            )}

            {modalTestResult && !testDirectMutation.isPending && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "0.75rem 1rem",
                  borderRadius: "6px",
                  fontSize: "0.875rem",
                  lineHeight: "1.4",
                  display: "flex",
                  alignItems: "flex-start",
                  gap: "0.65rem",
                  backgroundColor: modalTestResult.success
                    ? "rgba(40, 167, 69, 0.15)"
                    : "rgba(220, 53, 69, 0.15)",
                  color: modalTestResult.success
                    ? "var(--success, #28a745)"
                    : "var(--danger, #dc3545)",
                  border: `1px solid ${
                    modalTestResult.success
                      ? "rgba(40, 167, 69, 0.35)"
                      : "rgba(220, 53, 69, 0.35)"
                  }`,
                }}
              >
                <span
                  style={{
                    fontWeight: "bold",
                    fontSize: "1.1rem",
                    lineHeight: "1",
                  }}
                >
                  {modalTestResult.success ? "✓" : "✕"}
                </span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600 }}>
                    {modalTestResult.success
                      ? "Connection successful"
                      : "Connection failed"}
                  </div>
                  {modalTestResult.message && (
                    <div
                      style={{
                        marginTop: "0.25rem",
                        opacity: 0.95,
                        wordBreak: "break-word",
                      }}
                    >
                      {modalTestResult.message}
                    </div>
                  )}
                </div>
              </div>
            )}

            {(createMutation.isError || updateMutation.isError) && (
              <div className="modal-error">
                {(createMutation.error || updateMutation.error)?.message}
              </div>
            )}
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={handleModalTest}
                disabled={testDirectMutation.isPending}
              >
                {testDirectMutation.isPending ? "Testing..." : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  className="btn btn-outline btn-small"
                  onClick={() => {
                    setEditing(null);
                    setModalTestResult(null);
                  }}
                >
                  Cancel
                </button>
                <button
                  className="btn btn-primary btn-small"
                  onClick={handleSave}
                  disabled={createMutation.isPending || updateMutation.isPending}
                >
                  {createMutation.isPending || updateMutation.isPending
                    ? "Saving..."
                    : "Save"}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {editingRule && (
        <div className="modal-overlay" onClick={() => setEditingRule(null)}>
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 520,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid rgba(255, 255, 255, 0.12)",
            }}
          >
            <div
              className="modal-title"
              style={{ fontSize: "1.2rem", marginBottom: "1rem" }}
            >
              {editingRule.id ? "Edit RSS Rule" : "Add RSS Rule"}
            </div>
            <TextInput
              label="Rule Name"
              value={editingRule.name || ""}
              onChange={(v) => setEditingRule({ ...editingRule, name: v })}
              placeholder="e.g. 1080p Web-DL Only"
            />
            <Toggle
              label="Enable Rule"
              checked={editingRule.isEnabled ?? true}
              onChange={(v) => setEditingRule({ ...editingRule, isEnabled: v })}
            />
            <TextInput
              label="Must Contain (Regex or Substring)"
              value={editingRule.mustContain || ""}
              onChange={(v) => setEditingRule({ ...editingRule, mustContain: v })}
              placeholder="e.g. 1080p|720p or (?i)h265"
              hint="Regex pattern or keyword releases must match to be accepted"
            />
            <TextInput
              label="Must Not Contain (Regex or Substring)"
              value={editingRule.mustNotContain || ""}
              onChange={(v) => setEditingRule({ ...editingRule, mustNotContain: v })}
              placeholder="e.g. CAM|TS|HDCAM"
              hint="Regex pattern or keyword releases must NOT match"
            />
            <NumberInput
              label="Minimum Seeders"
              value={editingRule.minSeeders ?? 1}
              onChange={(v) => setEditingRule({ ...editingRule, minSeeders: v })}
              min={0}
            />
            <NumberInput
              label="Minimum Size (Bytes)"
              value={editingRule.minSizeBytes ?? 0}
              onChange={(v) => setEditingRule({ ...editingRule, minSizeBytes: v })}
              min={0}
              hint="Minimum file size in bytes (0 = no minimum)"
            />
            <NumberInput
              label="Maximum Size (Bytes)"
              value={editingRule.maxSizeBytes ?? 0}
              onChange={(v) => setEditingRule({ ...editingRule, maxSizeBytes: v })}
              min={0}
              hint="Maximum file size in bytes (0 = no maximum)"
            />
            <NumberInput
              label="Max Age (Days)"
              value={editingRule.maxAgeDays ?? 0}
              onChange={(v) => setEditingRule({ ...editingRule, maxAgeDays: v })}
              min={0}
              hint="Maximum age of releases in days to match (0 = no limit)"
            />
            <NumberInput
              label="Category ID"
              value={editingRule.categoryId ?? 0}
              onChange={(v) => setEditingRule({ ...editingRule, categoryId: v })}
              min={0}
              hint="Category ID to assign to grabbed torrents (0 = default)"
            />
            <TextInput
              label="Assigned Indexer IDs (Comma-separated)"
              value={
                Array.isArray(editingRule.indexerIds)
                  ? editingRule.indexerIds.join(",")
                  : ""
              }
              onChange={(v) => {
                const ids = v
                  .split(",")
                  .map((s) => Number(s.trim()))
                  .filter((n) => !isNaN(n) && n > 0);
                setEditingRule({ ...editingRule, indexerIds: ids });
              }}
              placeholder="e.g. 1, 2 (leave blank for all indexers)"
              hint="Comma-separated IDs of specific indexers this rule applies to"
            />
            <Toggle
              label="Freeleech Only"
              checked={editingRule.freeleechOnly ?? false}
              onChange={(v) => setEditingRule({ ...editingRule, freeleechOnly: v })}
            />

            {(createRuleMutation.isError || updateRuleMutation.isError) && (
              <div className="modal-error">
                {(createRuleMutation.error || updateRuleMutation.error)?.message}
              </div>
            )}
            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "0.5rem",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setEditingRule(null)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSaveRule}
                disabled={createRuleMutation.isPending || updateRuleMutation.isPending}
              >
                {createRuleMutation.isPending || updateRuleMutation.isPending
                  ? "Saving..."
                  : "Save"}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
