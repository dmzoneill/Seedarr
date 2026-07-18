import { useState } from 'react';
import {
  useDownloadClients,
  useCreateDownloadClient,
  useUpdateDownloadClient,
  useDeleteDownloadClient,
  useTestDownloadClient,
} from '../../api/hooks';
import type { DownloadClientDefinition } from '../../api/types';
import { TextInput, SelectInput, Toggle, NumberInput } from './shared';

export function DownloadClientsTab() {
  const { data: clients, isLoading } = useDownloadClients();
  const createMutation = useCreateDownloadClient();
  const updateMutation = useUpdateDownloadClient();
  const deleteMutation = useDeleteDownloadClient();
  const testMutation = useTestDownloadClient();
  const [editing, setEditing] = useState<Partial<DownloadClientDefinition> | null>(null);
  const [testResults, setTestResults] = useState<Record<number, boolean | null>>({});

  const defaultClient: Partial<DownloadClientDefinition> = {
    name: '',
    clientType: 'QBitTorrent',
    host: 'localhost',
    port: 8080,
    useSsl: false,
    username: '',
    password: '',
    category: '',
    enable: true,
  };

  const clientDefaults: Record<string, { port: number }> = {
    QBitTorrent: { port: 8080 },
    Transmission: { port: 9091 },
    Deluge: { port: 8112 },
  };

  const handleSave = () => {
    if (!editing) return;
    if (editing.id) {
      updateMutation.mutate(editing as DownloadClientDefinition, { onSuccess: () => setEditing(null) });
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
        <h3>Download Clients</h3>

        <div className="provider-cards">
          {clients?.map((client) => (
            <div key={client.id} className="provider-card" onClick={() => setEditing({ ...client })}>
              <div className="provider-card-actions">
                <button
                  className="provider-card-action"
                  title="Test"
                  onClick={(e) => { e.stopPropagation(); handleTest(client.id); }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete"
                  onClick={(e) => { e.stopPropagation(); deleteMutation.mutate(client.id); }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{client.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">{client.clientType}</span>
                {client.enable && <span className="provider-card-badge provider-card-badge-blue">Enabled</span>}
                {!client.enable && <span className="provider-card-badge provider-card-badge-gray">Disabled</span>}
                {client.useSsl && <span className="provider-card-badge provider-card-badge-amber">SSL</span>}
              </div>
              <div className="provider-card-info">{client.host}:{client.port}</div>
              {testResults[client.id] === true && <div className="provider-card-test provider-card-test-ok">Test passed</div>}
              {testResults[client.id] === false && <div className="provider-card-test provider-card-test-fail">Test failed</div>}
              {testResults[client.id] === null && <div className="provider-card-test provider-card-test-pending">Testing...</div>}
            </div>
          ))}
          <div className="provider-card-add" onClick={() => setEditing({ ...defaultClient })}>
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </div>

      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-title">{editing.id ? 'Edit Download Client' : 'Add Download Client'}</div>
            <TextInput label="Name" value={editing.name || ''} onChange={(v) => setEditing({ ...editing, name: v })} placeholder="My qBittorrent" />
            <SelectInput
              label="Client Type"
              value={editing.clientType || 'QBitTorrent'}
              onChange={(v) => setEditing({ ...editing, clientType: v, port: clientDefaults[v]?.port || editing.port || 8080 })}
              options={[
                { value: 'QBitTorrent', label: 'qBittorrent' },
                { value: 'Transmission', label: 'Transmission' },
                { value: 'Deluge', label: 'Deluge' },
              ]}
            />
            <TextInput label="Host" value={editing.host || ''} onChange={(v) => setEditing({ ...editing, host: v })} placeholder="localhost" />
            <NumberInput label="Port" value={editing.port || 8080} onChange={(v) => setEditing({ ...editing, port: v })} min={1} max={65535} />
            <Toggle label="Use SSL" checked={editing.useSsl ?? false} onChange={(v) => setEditing({ ...editing, useSsl: v })} />
            <TextInput label="Username" value={editing.username || ''} onChange={(v) => setEditing({ ...editing, username: v })} />
            <TextInput label="Password" value={editing.password || ''} onChange={(v) => setEditing({ ...editing, password: v })} type="password" />
            <TextInput label="Category" value={editing.category || ''} onChange={(v) => setEditing({ ...editing, category: v })} hint="Filter by category" />
            <Toggle label="Enabled" checked={editing.enable ?? true} onChange={(v) => setEditing({ ...editing, enable: v })} />
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
