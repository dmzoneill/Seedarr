import { useState } from 'react';
import {
  useArrConnections,
  useCreateArrConnection,
  useUpdateArrConnection,
  useDeleteArrConnection,
  useTestArrConnection,
  useArrSync,
} from '../../api/hooks';
import type { ArrConnection } from '../../api/types';
import { TextInput, SelectInput, Toggle } from './shared';

export function ConnectionsTab() {
  const { data: connections, isLoading } = useArrConnections();
  const createMutation = useCreateArrConnection();
  const updateMutation = useUpdateArrConnection();
  const deleteMutation = useDeleteArrConnection();
  const testMutation = useTestArrConnection();
  const syncMutation = useArrSync();
  const [editing, setEditing] = useState<Partial<ArrConnection> | null>(null);
  const [testResults, setTestResults] = useState<Record<number, boolean | null>>({});

  const defaultConnection: Partial<ArrConnection> = {
    name: '',
    arrType: 'Sonarr',
    url: 'http://localhost:8989',
    apiKey: '',
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
    implementation: 'SonarrConnection',
    configContract: 'ArrConnectionDefinition',
  };

  const handleSave = () => {
    if (!editing) return;
    if (editing.id) {
      updateMutation.mutate(editing as ArrConnection, { onSuccess: () => setEditing(null) });
    } else {
      createMutation.mutate(editing, { onSuccess: () => setEditing(null) });
    }
  };

  const handleTest = (id: number) => {
    setTestResults((prev) => ({ ...prev, [id]: null }));
    testMutation.mutate(id, {
      onSuccess: (data) => setTestResults((prev) => ({ ...prev, [id]: data.success })),
      onError: () => setTestResults((prev) => ({ ...prev, [id]: false })),
    });
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <>
      <div className="card">
        <div className="provider-section-header">
          <h3>Arr Connections</h3>
          <button
            className="btn btn-small"
            onClick={() => syncMutation.mutate()}
            disabled={syncMutation.isPending}
          >
            {syncMutation.isPending ? 'Syncing...' : 'Sync Now'}
          </button>
          {syncMutation.isError && (
            <span style={{ color: 'var(--danger)', fontSize: '0.85rem' }}>
              Sync failed: {syncMutation.error?.message}
            </span>
          )}
          {syncMutation.isSuccess && (
            <span style={{ color: 'var(--success)', fontSize: '0.85rem' }}>
              Sync complete
            </span>
          )}
        </div>

        <div className="provider-cards">
          {connections?.map((conn) => (
            <div key={conn.id} className="provider-card" onClick={() => setEditing({ ...conn })}>
              <div className="provider-card-actions">
                <button
                  className="provider-card-action"
                  title="Test"
                  onClick={(e) => { e.stopPropagation(); handleTest(conn.id); }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete"
                  onClick={(e) => { e.stopPropagation(); deleteMutation.mutate(conn.id); }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{conn.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">{conn.arrType}</span>
                {conn.syncEnabled && <span className="provider-card-badge provider-card-badge-blue">Sync</span>}
                {conn.enableAutomaticAdd && <span className="provider-card-badge provider-card-badge-blue">Auto Add</span>}
                {conn.webhookEnabled && <span className="provider-card-badge provider-card-badge-blue">Webhook</span>}
              </div>
              <div className="provider-card-info">{conn.url}</div>
              {testResults[conn.id] === true && <div className="provider-card-test provider-card-test-ok">Test passed</div>}
              {testResults[conn.id] === false && <div className="provider-card-test provider-card-test-fail">Test failed</div>}
              {testResults[conn.id] === null && <div className="provider-card-test provider-card-test-pending">Testing...</div>}
            </div>
          ))}
          <div className="provider-card-add" onClick={() => setEditing({ ...defaultConnection })}>
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </div>

      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-title">{editing.id ? 'Edit Connection' : 'Add Connection'}</div>
            <TextInput label="Name" value={editing.name || ''} onChange={(v) => setEditing({ ...editing, name: v })} placeholder="My Sonarr" />
            <SelectInput
              label="Type"
              value={editing.arrType || 'Sonarr'}
              onChange={(v) => {
                const defaults: Record<string, string> = { Sonarr: 'http://localhost:8989', Radarr: 'http://localhost:7878', Lidarr: 'http://localhost:8686' };
                setEditing({ ...editing, arrType: v, url: defaults[v] || editing.url || '', implementation: `${v}Connection` });
              }}
              options={[
                { value: 'Sonarr', label: 'Sonarr' },
                { value: 'Radarr', label: 'Radarr' },
                { value: 'Lidarr', label: 'Lidarr' },
              ]}
            />
            <TextInput label="URL" value={editing.url || ''} onChange={(v) => setEditing({ ...editing, url: v })} placeholder="http://localhost:8989" />
            <TextInput label="API Key" value={editing.apiKey || ''} onChange={(v) => setEditing({ ...editing, apiKey: v })} type="password" />
            <Toggle label="Sync Enabled" checked={editing.syncEnabled ?? true} onChange={(v) => setEditing({ ...editing, syncEnabled: v })} />
            <Toggle label="Auto Add" checked={editing.enableAutomaticAdd ?? true} onChange={(v) => setEditing({ ...editing, enableAutomaticAdd: v })} />
            <Toggle label="Webhook" checked={editing.webhookEnabled ?? true} onChange={(v) => setEditing({ ...editing, webhookEnabled: v })} />
            {editing.webhookEnabled !== false && (
              <TextInput label="Webhook Host" value={editing.webhookHost || ''} onChange={(v) => setEditing({ ...editing, webhookHost: v })} placeholder="seedarr.local" hint="Overrides default container IP" />
            )}
            {(createMutation.isError || updateMutation.isError) && (
              <div className="modal-error">{(createMutation.error || updateMutation.error)?.message}</div>
            )}
            <div className="modal-actions">
              <button className="btn" onClick={() => setEditing(null)}>Cancel</button>
              <button className="btn btn-success" onClick={handleSave} disabled={createMutation.isPending || updateMutation.isPending}>
                {createMutation.isPending || updateMutation.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
