import { useState } from "react";
import {
  useDownloadClients,
  useCreateDownloadClient,
  useUpdateDownloadClient,
  useDeleteDownloadClient,
  useTestDownloadClient,
  useTestDirectDownloadClient,
  useDownloadClientSync,
  useTags,
  useRemotePathMappings,
  useCreateRemotePathMapping,
  useUpdateRemotePathMapping,
  useDeleteRemotePathMapping,
  useTestRemotePathMapping,
} from "../../api/hooks";
import type {
  DownloadClientDefinition,
  DownloadClientTestResult,
  RemotePathMapping,
  RemotePathMappingTestResult,
} from "../../api/types";
import { useToast } from "../../context/ToastContext";
import { trackDownloadClientAction } from "../../utils/analytics";
import {
  TextInput,
  SelectInput,
  Toggle,
  NumberInput,
  SectionCard,
} from "./shared";
import { getDownloadClientUrl } from "../../utils/arrLinks";

export function DownloadClientsTab() {
  const { showToast } = useToast();
  const { data: clients, isLoading } = useDownloadClients();
  const { data: allTags } = useTags();
  const createMutation = useCreateDownloadClient();
  const updateMutation = useUpdateDownloadClient();
  const deleteMutation = useDeleteDownloadClient();
  const testMutation = useTestDownloadClient();
  const testDirectMutation = useTestDirectDownloadClient();
  const syncMutation = useDownloadClientSync();

  const { data: mappings, isLoading: isMappingsLoading } =
    useRemotePathMappings();
  const createMappingMutation = useCreateRemotePathMapping();
  const updateMappingMutation = useUpdateRemotePathMapping();
  const deleteMappingMutation = useDeleteRemotePathMapping();
  const testMappingMutation = useTestRemotePathMapping();

  const [editing, setEditing] =
    useState<Partial<DownloadClientDefinition> | null>(null);
  const [testResults, setTestResults] = useState<
    Record<number, DownloadClientTestResult | null>
  >({});
  const [modalTestResult, setModalTestResult] =
    useState<DownloadClientTestResult | null>(null);

  const [editingMapping, setEditingMapping] =
    useState<Partial<RemotePathMapping> | null>(null);
  const [testModalOpen, setTestModalOpen] = useState(false);
  const [testHost, setTestHost] = useState("");
  const [testPath, setTestPath] = useState("");
  const [testDirection, setTestDirection] = useState("remoteToLocal");
  const [mappingTestResult, setMappingTestResult] =
    useState<RemotePathMappingTestResult | null>(null);

  const defaultClient: Partial<DownloadClientDefinition> = {
    name: "",
    clientType: "QBitTorrent",
    host: "localhost",
    port: 8080,
    useSsl: false,
    username: "",
    password: "",
    category: "",
    enable: true,
    tags: [],
  };

  const clientDefaults: Record<string, { port: number }> = {
    QBitTorrent: { port: 8080 },
    Transmission: { port: 9091 },
    Deluge: { port: 8112 },
  };

  const handleOpenModal = (client: Partial<DownloadClientDefinition>) => {
    setModalTestResult(null);
    setEditing({ ...client, tags: client.tags || [] });
  };

  const handleSave = () => {
    if (!editing) return;
    const clientType = editing.clientType || "unknown";
    if (editing.id) {
      updateMutation.mutate(editing as DownloadClientDefinition, {
        onSuccess: () => {
          trackDownloadClientAction(clientType, "add");
          setEditing(null);
        },
      });
    } else {
      createMutation.mutate(editing, {
        onSuccess: () => {
          trackDownloadClientAction(clientType, "add");
          setEditing(null);
        },
      });
    }
  };

  const handleTest = (id: number) => {
    setTestResults((prev) => ({ ...prev, [id]: null }));
    testMutation.mutate(id, {
      onSuccess: (data) => setTestResults((prev) => ({ ...prev, [id]: data })),
      onError: (err) =>
        setTestResults((prev) => ({
          ...prev,
          [id]: { success: false, message: err.message },
        })),
    });
  };

  const handleModalTest = () => {
    if (!editing) return;
    setModalTestResult(null);
    const clientType = editing.clientType || "unknown";
    testDirectMutation.mutate(editing, {
      onSuccess: (data) => {
        trackDownloadClientAction(clientType, "test", data.success);
        setModalTestResult(data);
      },
      onError: (err) => {
        trackDownloadClientAction(clientType, "test", false);
        setModalTestResult({
          success: false,
          message: err.message || "Connection test failed",
        });
      },
    });
  };

  const defaultMapping: Partial<RemotePathMapping> = {
    host: "",
    remotePath: "",
    localPath: "",
  };

  const handleOpenMappingModal = (mapping: Partial<RemotePathMapping>) => {
    setEditingMapping({ ...mapping });
  };

  const handleSaveMapping = () => {
    if (!editingMapping) return;
    if (
      !editingMapping.host?.trim() ||
      !editingMapping.remotePath?.trim() ||
      !editingMapping.localPath?.trim()
    ) {
      showToast("Host, Remote Path, and Local Path are all required", "error");
      return;
    }

    if (editingMapping.id) {
      updateMappingMutation.mutate(editingMapping as RemotePathMapping, {
        onSuccess: () => {
          showToast("Remote path mapping updated", "success");
          setEditingMapping(null);
        },
        onError: (err) =>
          showToast(err.message || "Failed to update mapping", "error"),
      });
    } else {
      createMappingMutation.mutate(editingMapping, {
        onSuccess: () => {
          showToast("Remote path mapping created", "success");
          setEditingMapping(null);
        },
        onError: (err) =>
          showToast(err.message || "Failed to create mapping", "error"),
      });
    }
  };

  const handleDeleteMapping = (mapping: RemotePathMapping) => {
    if (!mapping.id) return;
    if (
      window.confirm(
        `Are you sure you want to delete path mapping for host "${mapping.host}" (${mapping.remotePath} -> ${mapping.localPath})?`,
      )
    ) {
      deleteMappingMutation.mutate(mapping.id, {
        onSuccess: () => showToast("Remote path mapping deleted", "success"),
        onError: (err) =>
          showToast(err.message || "Failed to delete mapping", "error"),
      });
    }
  };

  const handleOpenTestModal = (host = "", samplePath = "") => {
    setTestHost(
      host || (clients && clients.length > 0 ? clients[0].host : "localhost"),
    );
    setTestPath(samplePath || "/downloads/sample-movie/video.mkv");
    setTestDirection("remoteToLocal");
    setMappingTestResult(null);
    setTestModalOpen(true);
  };

  const handleRunMappingTest = () => {
    if (!testHost.trim() || !testPath.trim()) {
      showToast("Host and Sample Path are required for testing", "error");
      return;
    }
    setMappingTestResult(null);
    testMappingMutation.mutate(
      {
        host: testHost.trim(),
        path: testPath.trim(),
        direction: testDirection,
      },
      {
        onSuccess: (data) => setMappingTestResult(data),
        onError: (err) =>
          showToast(err.message || "Path translation test failed", "error"),
      },
    );
  };

  if (isLoading)
    return <div className="loading">Loading download clients...</div>;

  return (
    <>
      <SectionCard
        title="BitTorrent Download Clients"
        description="Connect external download clients (qBittorrent, Transmission, Deluge) to import active seeding state"
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          <button
            className="btn btn-outline btn-small"
            onClick={() => {
              syncMutation.mutate(undefined, {
                onSuccess: (res) =>
                  showToast(
                    `Sync Complete: ${res.added} added, ${res.skipped} skipped, ${res.failed} failed`,
                    "success",
                  ),
                onError: (err) =>
                  showToast(err.message || "Torrent sync failed", "error"),
              });
            }}
            disabled={syncMutation.isPending}
            title="Import torrents currently in your download clients"
          >
            {syncMutation.isPending ? "Syncing..." : "🔄 Sync Torrents"}
          </button>
        </div>

        <div className="provider-cards">
          {clients?.map((client) => (
            <div
              key={client.id}
              className="provider-card"
              onClick={() => handleOpenModal(client)}
            >
              <div className="provider-card-actions">
                {client.host && (
                  <a
                    href={getDownloadClientUrl(client)}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="provider-card-action"
                    title={`Open ${client.name} Web UI`}
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
                    handleTest(client.id);
                  }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete Client"
                  onClick={(e) => {
                    e.stopPropagation();
                    if (
                      window.confirm(
                        `Are you sure you want to delete download client "${client.name}"?`,
                      )
                    ) {
                      deleteMutation.mutate(client.id, {
                        onSuccess: () =>
                          showToast("Download client deleted", "success"),
                        onError: () =>
                          showToast("Failed to delete download client", "error"),
                      });
                    }
                  }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{client.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">
                  {client.clientType}
                </span>
                {client.enable && (
                  <span className="provider-card-badge provider-card-badge-blue">
                    Enabled
                  </span>
                )}
                {!client.enable && (
                  <span className="provider-card-badge provider-card-badge-gray">
                    Disabled
                  </span>
                )}
                {client.useSsl && (
                  <span className="provider-card-badge provider-card-badge-amber">
                    SSL
                  </span>
                )}
                {client.backoffUntil && new Date(client.backoffUntil).getTime() > Date.now() ? (
                  <span
                    className="provider-card-badge provider-card-badge-amber"
                    title={`In circuit breaker backoff until ${new Date(client.backoffUntil).toLocaleTimeString()} (${client.consecutiveFailures ?? 0} failures)`}
                  >
                    Backoff ({client.consecutiveFailures ?? 0})
                  </span>
                ) : client.isOnline === true ? (
                  <span
                    className="provider-card-badge provider-card-badge-green"
                    title={client.version ? `Online • Version: ${client.version}` : "Client is online"}
                  >
                    Online
                  </span>
                ) : client.isOnline === false ? (
                  <span
                    className="provider-card-badge provider-card-badge-red"
                    title={client.lastErrorMessage || "Client is offline"}
                  >
                    Offline
                  </span>
                ) : null}
              </div>
              {client.tags && client.tags.length > 0 && allTags && (
                <div
                  style={{
                    display: "flex",
                    flexWrap: "wrap",
                    gap: "0.25rem",
                    marginTop: "0.35rem",
                  }}
                >
                  {client.tags.map((tagId) => {
                    const t = allTags.find((tag) => tag.id === tagId);
                    return t ? (
                      <span
                        key={tagId}
                        className="provider-card-badge provider-card-badge-gray"
                        style={{ fontSize: "0.7rem" }}
                      >
                        🏷️ {t.label}
                      </span>
                    ) : null;
                  })}
                </div>
              )}
              <div className="provider-card-info">
                <div>
                  {client.host}:{client.port}
                  {client.version ? ` • v${client.version}` : ""}
                </div>
                {client.lastSyncTime && (
                  <div
                    style={{
                      fontSize: "0.72rem",
                      color: "var(--text-muted)",
                      marginTop: "0.2rem",
                    }}
                  >
                    Last sync: {new Date(client.lastSyncTime).toLocaleTimeString()}
                  </div>
                )}
                {client.lastErrorMessage && client.isOnline === false && (
                  <div
                    style={{
                      fontSize: "0.72rem",
                      color: "var(--danger, #ef4444)",
                      marginTop: "0.2rem",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                    title={client.lastErrorMessage}
                  >
                    {client.lastErrorMessage}
                  </div>
                )}
              </div>
              {testResults[client.id]?.success === true && (
                <div className="provider-card-test provider-card-test-ok">
                  ✓ Connection passed
                </div>
              )}
              {testResults[client.id]?.success === false && (
                <div
                  className="provider-card-test provider-card-test-fail"
                  title={testResults[client.id]?.message}
                >
                  ✕ Connection failed
                </div>
              )}
              {testResults[client.id] === null && (
                <div className="provider-card-test provider-card-test-pending">
                  Testing...
                </div>
              )}
            </div>
          ))}
          <div
            className="provider-card-add"
            onClick={() => handleOpenModal(defaultClient)}
            title="Add Download Client"
          >
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </SectionCard>

      <SectionCard
        title="Remote Path Mappings"
        description="Translate directory paths between remote download clients and Seedarr's local filesystem"
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          <span style={{ fontSize: "0.85rem", color: "var(--text-secondary)" }}>
            Configure host-specific path mappings for remote download clients (e.g. Docker mount paths or remote seedboxes).
          </span>
          <div style={{ display: "flex", gap: "0.5rem" }}>
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={() => handleOpenTestModal()}
            >
              🧪 Test Path Mapping
            </button>
            <button
              type="button"
              className="btn btn-primary btn-small"
              onClick={() => handleOpenMappingModal(defaultMapping)}
            >
              + Add Mapping
            </button>
          </div>
        </div>

        {isMappingsLoading ? (
          <div
            style={{
              padding: "1rem",
              textAlign: "center",
              color: "var(--text-muted)",
              fontSize: "0.85rem",
            }}
          >
            Loading remote path mappings...
          </div>
        ) : !mappings || mappings.length === 0 ? (
          <div
            style={{
              textAlign: "center",
              padding: "2rem",
              color: "var(--text-muted)",
              fontSize: "0.85rem",
              backgroundColor: "rgba(255, 255, 255, 0.02)",
              borderRadius: "6px",
              border: "1px dashed var(--border-light)",
            }}
          >
            No remote path mappings configured. Click <strong>+ Add Mapping</strong> to configure path translation.
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table
              className="table"
              style={{
                width: "100%",
                borderCollapse: "collapse",
                fontSize: "0.85rem",
              }}
            >
              <thead>
                <tr
                  style={{
                    borderBottom: "1px solid var(--border-light)",
                    textAlign: "left",
                    color: "var(--text-muted, #7e8092)",
                    fontSize: "0.8rem",
                  }}
                >
                  <th style={{ padding: "0.6rem 0.8rem" }}>Host</th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>Remote Path</th>
                  <th style={{ padding: "0.6rem 0.8rem" }}>Local Path</th>
                  <th style={{ padding: "0.6rem 0.8rem", textAlign: "right" }}>
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {mappings.map((mapping) => (
                  <tr
                    key={mapping.id}
                    style={{ borderBottom: "1px solid var(--border-light)" }}
                  >
                    <td style={{ padding: "0.65rem 0.8rem", fontWeight: 600 }}>
                      {mapping.host}
                    </td>
                    <td
                      style={{
                        padding: "0.65rem 0.8rem",
                        fontFamily: "monospace",
                        fontSize: "0.8rem",
                      }}
                    >
                      {mapping.remotePath}
                    </td>
                    <td
                      style={{
                        padding: "0.65rem 0.8rem",
                        fontFamily: "monospace",
                        fontSize: "0.8rem",
                      }}
                    >
                      {mapping.localPath}
                    </td>
                    <td
                      style={{
                        padding: "0.65rem 0.8rem",
                        textAlign: "right",
                      }}
                    >
                      <div
                        style={{
                          display: "inline-flex",
                          gap: "0.4rem",
                          justifyContent: "flex-end",
                        }}
                      >
                        <button
                          type="button"
                          className="btn btn-outline btn-small"
                          style={{
                            padding: "0.25rem 0.55rem",
                            fontSize: "0.75rem",
                          }}
                          title="Test translation with this mapping"
                          onClick={() =>
                            handleOpenTestModal(mapping.host, mapping.remotePath)
                          }
                        >
                          Test
                        </button>
                        <button
                          type="button"
                          className="btn btn-outline btn-small"
                          style={{
                            padding: "0.25rem 0.55rem",
                            fontSize: "0.75rem",
                          }}
                          title="Edit mapping"
                          onClick={() => handleOpenMappingModal(mapping)}
                        >
                          Edit
                        </button>
                        <button
                          type="button"
                          className="btn btn-danger btn-small"
                          style={{
                            padding: "0.25rem 0.55rem",
                            fontSize: "0.75rem",
                          }}
                          title="Delete mapping"
                          onClick={() => handleDeleteMapping(mapping)}
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>

      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(null)}>
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
              {editing.id ? "Edit Download Client" : "Add Download Client"}
            </div>
            <TextInput
              label="Name"
              value={editing.name || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, name: v });
              }}
              placeholder="My qBittorrent"
            />
            <SelectInput
              label="Client Type"
              value={editing.clientType || "QBitTorrent"}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({
                  ...editing,
                  clientType: v,
                  port: clientDefaults[v]?.port || editing.port || 8080,
                });
              }}
              options={[
                { value: "QBitTorrent", label: "qBittorrent" },
                { value: "Transmission", label: "Transmission" },
                { value: "Deluge", label: "Deluge" },
              ]}
            />
            <TextInput
              label="Host"
              value={editing.host || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, host: v });
              }}
              placeholder="localhost"
            />
            <NumberInput
              label="Port"
              value={editing.port || 8080}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, port: v });
              }}
              min={1}
              max={65535}
            />
            <Toggle
              label="Use SSL"
              checked={editing.useSsl ?? false}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, useSsl: v });
              }}
            />
            <TextInput
              label="URL Base"
              value={editing.urlBase || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, urlBase: v });
              }}
              placeholder="e.g. qbittorrent or transmission/web"
              hint="Subpath for reverse proxies or subpath-hosted Web UIs"
            />
            {editing.clientType !== "Deluge" && (
              <TextInput
                label="Username"
                value={editing.username || ""}
                onChange={(v) => {
                  setModalTestResult(null);
                  setEditing({ ...editing, username: v });
                }}
              />
            )}
            <TextInput
              label="Password"
              value={editing.password || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, password: v });
              }}
              type="password"
            />
            <TextInput
              label="Category"
              value={editing.category || ""}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, category: v });
              }}
              hint="Filter by category"
            />
            <div className="form-group">
              <label className="form-label">Tags</label>
              <div className="form-input-wrapper">
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
                  }}
                  style={{ borderRadius: "6px", minHeight: "80px" }}
                >
                  {allTags?.map((tag) => (
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
                      const tag = allTags?.find((t) => t.id === tagId);
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
                          }}
                        >
                          {tag.label} ✕
                        </span>
                      ) : null;
                    })}
                  </div>
                )}
                <span className="form-hint">
                  Assign tags to this download client (hold Ctrl/Cmd to select multiple)
                </span>
              </div>
            </div>
            <Toggle
              label="Enabled"
              checked={editing.enable ?? true}
              onChange={(v) => {
                setModalTestResult(null);
                setEditing({ ...editing, enable: v });
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
                <span>
                  Testing connection to {editing.host || "localhost"}:
                  {editing.port || 8080}...
                </span>
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
                      ? "Connection Successful"
                      : "Connection Failed"}
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
                {testDirectMutation.isPending
                  ? "Testing..."
                  : "Test Connection"}
              </button>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button
                  className="btn btn-outline btn-small"
                  onClick={() => setEditing(null)}
                >
                  Cancel
                </button>
                <button
                  className="btn btn-primary btn-small"
                  onClick={handleSave}
                  disabled={
                    createMutation.isPending || updateMutation.isPending
                  }
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

      {editingMapping && (
        <div
          className="modal-overlay"
          onClick={() => setEditingMapping(null)}
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
              {editingMapping.id
                ? "Edit Remote Path Mapping"
                : "Add Remote Path Mapping"}
            </div>
            <TextInput
              label="Host"
              value={editingMapping.host || ""}
              onChange={(v) =>
                setEditingMapping({ ...editingMapping, host: v })
              }
              placeholder="e.g. localhost, 192.168.1.100, or seedbox"
              hint="The download client host as configured in Seedarr (must match client Host)."
            />
            <TextInput
              label="Remote Path"
              value={editingMapping.remotePath || ""}
              onChange={(v) =>
                setEditingMapping({ ...editingMapping, remotePath: v })
              }
              placeholder="e.g. /downloads or D:\Torrents"
              hint="Path prefix reported by the remote download client."
            />
            <TextInput
              label="Local Path"
              value={editingMapping.localPath || ""}
              onChange={(v) =>
                setEditingMapping({ ...editingMapping, localPath: v })
              }
              placeholder="e.g. /media/downloads or /mnt/storage/downloads"
              hint="Local path prefix where files are accessible by Seedarr on disk."
            />

            {(createMappingMutation.isError ||
              updateMappingMutation.isError) && (
              <div className="modal-error">
                {(createMappingMutation.error || updateMappingMutation.error)
                  ?.message}
              </div>
            )}

            <div
              className="modal-actions"
              style={{
                display: "flex",
                justifyContent: "flex-end",
                alignItems: "center",
                gap: "0.5rem",
                marginTop: "1.5rem",
              }}
            >
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setEditingMapping(null)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary btn-small"
                onClick={handleSaveMapping}
                disabled={
                  createMappingMutation.isPending ||
                  updateMappingMutation.isPending
                }
              >
                {createMappingMutation.isPending ||
                updateMappingMutation.isPending
                  ? "Saving..."
                  : "Save"}
              </button>
            </div>
          </div>
        </div>
      )}

      {testModalOpen && (
        <div
          className="modal-overlay"
          onClick={() => setTestModalOpen(false)}
        >
          <div
            className="modal"
            onClick={(e) => e.stopPropagation()}
            style={{
              maxWidth: 580,
              borderRadius: "8px",
              boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
              border: "1px solid var(--border-light)",
            }}
          >
            <div
              className="modal-title"
              style={{ fontSize: "1.2rem", marginBottom: "0.5rem" }}
            >
              🧪 Test Remote Path Translation
            </div>
            <p
              style={{
                fontSize: "0.85rem",
                color: "var(--text-secondary)",
                marginBottom: "1rem",
              }}
            >
              Simulate path translation for a download client and verify whether the resolved local path exists on disk.
            </p>

            <TextInput
              label="Host"
              value={testHost}
              onChange={(v) => {
                setTestHost(v);
                setMappingTestResult(null);
              }}
              placeholder="e.g. localhost, 192.168.1.100, or seedbox"
              hint="Host name or IP to match against configured mapping rules."
            />
            <TextInput
              label="Sample Path"
              value={testPath}
              onChange={(v) => {
                setTestPath(v);
                setMappingTestResult(null);
              }}
              placeholder="e.g. /downloads/Torrents/Sample.mkv"
              hint="Path to translate."
            />
            <SelectInput
              label="Direction"
              value={testDirection}
              onChange={(v) => {
                setTestDirection(v);
                setMappingTestResult(null);
              }}
              options={[
                {
                  value: "remoteToLocal",
                  label: "Remote to Local (Download Client → Seedarr)",
                },
                {
                  value: "localToRemote",
                  label: "Local to Remote (Seedarr → Download Client)",
                },
              ]}
            />

            {mappingTestResult && (
              <div
                style={{
                  marginTop: "1rem",
                  padding: "1rem",
                  borderRadius: "8px",
                  border: "1px solid var(--border-light)",
                  backgroundColor: "var(--bg-primary, #0f111c)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.5rem",
                    marginBottom: "0.75rem",
                  }}
                >
                  {mappingTestResult.ruleApplied &&
                    mappingTestResult.localPathExists && (
                      <span
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.35rem",
                          padding: "0.3rem 0.65rem",
                          borderRadius: "4px",
                          fontSize: "0.8rem",
                          fontWeight: 600,
                          backgroundColor: "rgba(34, 197, 94, 0.15)",
                          color: "#22c55e",
                          border: "1px solid rgba(34, 197, 94, 0.3)",
                        }}
                      >
                        <span>✓</span> Rule matched &amp; local path verified on disk
                      </span>
                    )}
                  {mappingTestResult.ruleApplied &&
                    !mappingTestResult.localPathExists && (
                      <span
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: "0.35rem",
                          padding: "0.3rem 0.65rem",
                          borderRadius: "4px",
                          fontSize: "0.8rem",
                          fontWeight: 600,
                          backgroundColor: "rgba(245, 158, 11, 0.15)",
                          color: "#f59e0b",
                          border: "1px solid rgba(245, 158, 11, 0.3)",
                        }}
                      >
                        <span>⚠️</span> Rule matched, but local directory/file not found on disk
                      </span>
                    )}
                  {!mappingTestResult.ruleApplied && (
                    <span
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.35rem",
                        padding: "0.3rem 0.65rem",
                        borderRadius: "4px",
                        fontSize: "0.8rem",
                        fontWeight: 600,
                        backgroundColor: "rgba(239, 68, 68, 0.15)",
                        color: "#ef4444",
                        border: "1px solid rgba(239, 68, 68, 0.3)",
                      }}
                    >
                      <span>✕</span> No rule matched (unmapped fallback)
                    </span>
                  )}
                </div>

                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.5rem",
                    fontSize: "0.82rem",
                  }}
                >
                  <div>
                    <span
                      style={{
                        color: "var(--text-muted)",
                        display: "block",
                        fontSize: "0.75rem",
                      }}
                    >
                      Input Path:
                    </span>
                    <span
                      style={{
                        fontFamily: "monospace",
                        color: "var(--text-secondary)",
                      }}
                    >
                      {mappingTestResult.inputPath}
                    </span>
                  </div>
                  <div>
                    <span
                      style={{
                        color: "var(--text-muted)",
                        display: "block",
                        fontSize: "0.75rem",
                      }}
                    >
                      Mapped Path:
                    </span>
                    <span
                      style={{
                        fontFamily: "monospace",
                        color: "var(--text-primary)",
                        fontWeight: 600,
                      }}
                    >
                      {mappingTestResult.mappedPath}
                    </span>
                  </div>
                  {mappingTestResult.ruleApplied && (
                    <div
                      style={{
                        display: "grid",
                        gridTemplateColumns: "1fr 1fr",
                        gap: "0.5rem",
                        marginTop: "0.25rem",
                        padding: "0.5rem",
                        backgroundColor: "rgba(255, 255, 255, 0.02)",
                        borderRadius: "4px",
                      }}
                    >
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            fontSize: "0.75rem",
                          }}
                        >
                          Matched Rule:
                        </span>
                        <div>
                          #{mappingTestResult.matchedRuleId} ({mappingTestResult.matchedRuleHost})
                        </div>
                      </div>
                      <div>
                        <span
                          style={{
                            color: "var(--text-muted)",
                            fontSize: "0.75rem",
                          }}
                        >
                          Prefix Translation:
                        </span>
                        <div
                          style={{
                            fontFamily: "monospace",
                            fontSize: "0.75rem",
                          }}
                        >
                          {mappingTestResult.matchedRemotePrefix} &rarr;{" "}
                          {mappingTestResult.matchedLocalPrefix}
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            )}

            {testMappingMutation.isError && (
              <div className="modal-error" style={{ marginTop: "1rem" }}>
                {testMappingMutation.error?.message}
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
                className="btn btn-primary btn-small"
                onClick={handleRunMappingTest}
                disabled={testMappingMutation.isPending}
              >
                {testMappingMutation.isPending
                  ? "Translating..."
                  : "Translate / Test"}
              </button>
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => setTestModalOpen(false)}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
