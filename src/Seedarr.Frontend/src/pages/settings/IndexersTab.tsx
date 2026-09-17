import { useState, useMemo, useEffect } from "react";
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
  useCategories,
  useDownloadClients,
  useTags,
} from "../../api/hooks";
import type { IndexerDefinition, IndexerTestResult, RssRule } from "../../api/types";
import { TextInput, SelectInput, Toggle, NumberInput, SectionCard } from "./shared";
import { useToast } from "../../context/ToastContext";

export function IndexersTab() {
  const { showToast } = useToast();

  // Download clients
  const { data: downloadClients } = useDownloadClients();

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

  // RSS Rules, Categories, Tags
  const { data: rssRules, isLoading: isRssRulesLoading } = useRssRules();
  const { data: categories } = useCategories();
  const { data: tags } = useTags();
  const createRuleMutation = useCreateRssRule();
  const updateRuleMutation = useUpdateRssRule();
  const deleteRuleMutation = useDeleteRssRule();
  const syncRssMutation = useSyncRss();
  const [editingRule, setEditingRule] = useState<Partial<RssRule> | null>(null);
  const [syncCooldownRemaining, setSyncCooldownRemaining] = useState<number>(0);

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
    tags: [],
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
    tags: [],
    priority: 0,
  };

  const categoryOptions = useMemo(() => {
    const opts = [{ value: "0", label: "None / Default" }];
    if (categories) {
      categories.forEach((cat) => {
        opts.push({ value: cat.id.toString(), label: cat.name });
      });
    }
    return opts;
  }, [categories]);

  useEffect(() => {
    if (syncCooldownRemaining <= 0) return;
    const timer = setInterval(() => {
      setSyncCooldownRemaining((prev) => Math.max(0, prev - 1));
    }, 1000);
    return () => clearInterval(timer);
  }, [syncCooldownRemaining]);

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
    const minSeeders = Number(editingRule.minSeeders) || 0;
    const minSizeBytes = Number(editingRule.minSizeBytes) || 0;
    const maxSizeBytes = Number(editingRule.maxSizeBytes) || 0;
    const maxAgeDays = Number(editingRule.maxAgeDays) || 0;
    const priority = Number(editingRule.priority) || 0;

    if (minSeeders < 0 || minSizeBytes < 0 || maxSizeBytes < 0 || maxAgeDays < 0 || priority < 0) {
      showToast("Numerical constraints cannot be negative", "error");
      return;
    }

    if (maxSizeBytes > 0 && minSizeBytes > maxSizeBytes) {
      showToast("Minimum size cannot be greater than maximum size", "error");
      return;
    }

    const payload: Partial<RssRule> = {
      ...editingRule,
      name,
      isEnabled: editingRule.isEnabled ?? true,
      mustContain: editingRule.mustContain?.trim() || "",
      mustNotContain: editingRule.mustNotContain?.trim() || "",
      minSeeders,
      minSizeBytes,
      maxSizeBytes,
      maxAgeDays,
      priority,
      freeleechOnly: Boolean(editingRule.freeleechOnly),
      categoryId: Number(editingRule.categoryId) || 0,
      indexerIds: editingRule.indexerIds || [],
      tags: editingRule.tags || [],
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
    if (syncCooldownRemaining > 0 || syncRssMutation.isPending) return;
    syncRssMutation.mutate(undefined, {
      onSuccess: (res) => {
        setSyncCooldownRemaining(15);
        showToast(`RSS sync completed successfully (${res.grabbedCount} releases grabbed)`, "success");
      },
      onError: (err: any) => {
        setSyncCooldownRemaining(15);
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
                        onError: (err) => showToast(err?.message || "Failed to delete indexer", "error"),
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
            disabled={syncRssMutation.isPending || syncCooldownRemaining > 0}
          >
            {syncRssMutation.isPending
              ? "Syncing RSS..."
              : syncCooldownRemaining > 0
                ? `🔄 Sync RSS (${syncCooldownRemaining}s)`
                : "🔄 Sync RSS Now"}
          </button>
        </div>

        <div className="provider-cards">
          {[...(rssRules || [])]
            .sort((a, b) => (a.priority ?? 0) - (b.priority ?? 0) || a.id - b.id)
            .map((rule) => (
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
                <span className="provider-card-badge provider-card-badge-blue">
                  Priority {rule.priority ?? 0}
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
                    Cat: {categories?.find((c) => c.id === rule.categoryId)?.name || rule.categoryId}
                  </span>
                )}
                {rule.tags && rule.tags.length > 0 && (
                  <span className="provider-card-badge provider-card-badge-gold">
                    {rule.tags.length} {rule.tags.length === 1 ? "Tag" : "Tags"}
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
              border: "1px solid var(--border-light)",
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
            {editing.indexerType !== "Prowlarr" && (
              <TextInput
                label="API Path"
                value={editing.apiPath || "/api"}
                onChange={(v) => {
                  setEditing({ ...editing, apiPath: v });
                  setModalTestResult(null);
                }}
                placeholder="api"
              />
            )}
            <div className="form-group" style={{ marginBottom: "1rem" }}>
              <label
                className="form-label"
                style={{
                  display: "block",
                  marginBottom: "0.35rem",
                  fontSize: "0.85rem",
                  fontWeight: 500,
                  color: "var(--text-secondary)",
                }}
              >
                Download Client
              </label>
              <select
                className="form-select"
                value={editing.downloadClientId || 0}
                onChange={(e) => {
                  setEditing({
                    ...editing,
                    downloadClientId: Number(e.target.value),
                  });
                  setModalTestResult(null);
                }}
                style={{
                  width: "100%",
                  padding: "0.45rem 0.65rem",
                  borderRadius: "6px",
                  border: "1px solid var(--border-color)",
                  backgroundColor: "var(--bg-secondary)",
                  color: "var(--text-primary)",
                }}
              >
                <option value={0}>Default / None</option>
                {downloadClients?.map((client) => (
                  <option key={client.id} value={client.id}>
                    {client.name} ({client.clientType})
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group" style={{ marginBottom: "1rem" }}>
              <label
                className="form-label"
                style={{
                  display: "block",
                  marginBottom: "0.35rem",
                  fontSize: "0.85rem",
                  fontWeight: 500,
                  color: "var(--text-secondary)",
                }}
              >
                Tags
              </label>
              <select
                multiple
                className="form-select"
                value={(editing.tags || []).map(String)}
                onChange={(e) => {
                  const selectedOptions = Array.from(
                    e.target.selectedOptions,
                    (option) => Number(option.value),
                  );
                  setEditing({ ...editing, tags: selectedOptions });
                  setModalTestResult(null);
                }}
                style={{
                  width: "100%",
                  borderRadius: "6px",
                  minHeight: "75px",
                  border: "1px solid var(--border-color)",
                  backgroundColor: "var(--bg-secondary)",
                  color: "var(--text-primary)",
                }}
              >
                {tags?.map((tag) => (
                  <option key={tag.id} value={tag.id}>
                    {tag.label}
                  </option>
                ))}
              </select>
              {editing.tags && editing.tags.length > 0 && (
                <div
                  style={{
                    display: "flex",
                    flexWrap: "wrap",
                    gap: "0.35rem",
                    marginTop: "0.5rem",
                  }}
                >
                  {editing.tags.map((tagId) => {
                    const tag = tags?.find((t) => t.id === tagId);
                    return tag ? (
                      <span
                        key={tagId}
                        className="badge badge-primary"
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.3rem",
                          padding: "0.2rem 0.5rem",
                          fontSize: "0.75rem",
                          cursor: "pointer",
                        }}
                        title="Click to remove tag"
                        onClick={() => {
                          setEditing({
                            ...editing,
                            tags: (editing.tags || []).filter(
                              (id) => id !== tagId,
                            ),
                          });
                          setModalTestResult(null);
                        }}
                      >
                        {tag.label} ✕
                      </span>
                    ) : null;
                  })}
                </div>
              )}
            </div>
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
              border: "1px solid var(--border-light)",
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
              label="Priority"
              value={editingRule.priority ?? 0}
              onChange={(v) => setEditingRule({ ...editingRule, priority: v })}
              min={0}
              hint="Evaluation priority (lower numbers evaluate first, e.g. 0 before 10)"
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
            <SelectInput
              label="Category"
              value={String(editingRule.categoryId ?? 0)}
              onChange={(v) =>
                setEditingRule({ ...editingRule, categoryId: Number(v) || 0 })
              }
              options={categoryOptions}
              hint="Category to assign to grabbed torrents"
            />
            <div className="form-group">
              <label className="form-label">Tags</label>
              <div className="form-input-wrapper">
                {tags && tags.length > 0 ? (
                  <div
                    style={{
                      display: "flex",
                      flexWrap: "wrap",
                      gap: "0.4rem",
                      padding: "0.4rem 0",
                    }}
                  >
                    {tags.map((tag) => {
                      const isSelected = (editingRule.tags || []).includes(
                        tag.id,
                      );
                      return (
                        <button
                          key={tag.id}
                          type="button"
                          className={`badge ${
                            isSelected ? "badge-primary" : "badge-secondary"
                          }`}
                          style={{
                            cursor: "pointer",
                            padding: "0.3rem 0.6rem",
                            fontSize: "0.82rem",
                            borderRadius: "4px",
                            border: isSelected
                              ? "1px solid var(--accent)"
                              : "1px solid var(--border-light)",
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "0.35rem",
                            background: isSelected ? undefined : "transparent",
                          }}
                          onClick={() => {
                            const current = editingRule.tags || [];
                            const updated = isSelected
                              ? current.filter((id) => id !== tag.id)
                              : [...current, tag.id];
                            setEditingRule({ ...editingRule, tags: updated });
                          }}
                        >
                          <span>{isSelected ? "✓" : "+"}</span>
                          <span>{tag.label}</span>
                        </button>
                      );
                    })}
                  </div>
                ) : (
                  <span
                    style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}
                  >
                    No tags configured. Create tags in Settings &gt; Tags.
                  </span>
                )}
                <span className="form-hint">
                  Tags to apply to torrents grabbed by this rule
                </span>
              </div>
            </div>
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
