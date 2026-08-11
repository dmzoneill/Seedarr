import { useState } from 'react';
import {
  useIndexers,
  useCreateIndexer,
  useUpdateIndexer,
  useDeleteIndexer,
  useTestIndexer,
} from '../../api/hooks';
import type { IndexerDefinition } from '../../api/types';
import { TextInput, SelectInput, Toggle } from './shared';

export function IndexersTab() {
  const { data: indexers, isLoading } = useIndexers();
  const createMutation = useCreateIndexer();
  const updateMutation = useUpdateIndexer();
  const deleteMutation = useDeleteIndexer();
  const testMutation = useTestIndexer();
  const [editing, setEditing] = useState<Partial<IndexerDefinition> | null>(null);
  const [testResults, setTestResults] = useState<Record<number, boolean | null>>({});

  const defaultIndexer: Partial<IndexerDefinition> = {
    name: '',
    indexerType: 'Prowlarr',
    url: 'http://localhost:9696',
    apiKey: '',
    apiPath: '/api',
    enableRss: true,
    enableSearch: true,
    categories: '',
    downloadClientId: 0,
  };

  const handleSave = () => {
    if (!editing) return;
    const payload = {
      ...editing,
      implementation: `${editing.indexerType || 'Prowlarr'}Indexer`,
      configContract: 'IndexerDefinition',
    };
    if (editing.id) {
      updateMutation.mutate(payload as IndexerDefinition, { onSuccess: () => setEditing(null) });
    } else {
      createMutation.mutate(payload, { onSuccess: () => setEditing(null) });
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
          <h3>Indexers</h3>
        </div>

        <div className="provider-cards">
          {indexers?.map((idx) => (
            <div key={idx.id} className="provider-card" onClick={() => setEditing({ ...idx })}>
              <div className="provider-card-actions">
                <button
                  className="provider-card-action"
                  title="Test"
                  onClick={(e) => { e.stopPropagation(); handleTest(idx.id); }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete"
                  onClick={(e) => { e.stopPropagation(); deleteMutation.mutate(idx.id); }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{idx.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">{idx.indexerType}</span>
                {idx.enableRss && <span className="provider-card-badge provider-card-badge-blue">RSS</span>}
                {idx.enableSearch && <span className="provider-card-badge provider-card-badge-blue">Search</span>}
              </div>
              <div className="provider-card-info">{idx.url}</div>
              {testResults[idx.id] === true && <div className="provider-card-test provider-card-test-ok">Test passed</div>}
              {testResults[idx.id] === false && <div className="provider-card-test provider-card-test-fail">Test failed</div>}
              {testResults[idx.id] === null && <div className="provider-card-test provider-card-test-pending">Testing...</div>}
            </div>
          ))}
          <div className="provider-card-add" onClick={() => setEditing({ ...defaultIndexer })}>
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </div>

      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-title">{editing.id ? 'Edit Indexer' : 'Add Indexer'}</div>
            <TextInput label="Name" value={editing.name || ''} onChange={(v) => setEditing({ ...editing, name: v })} placeholder="My Prowlarr" />
            <SelectInput
              label="Type"
              value={editing.indexerType || 'Prowlarr'}
              onChange={(v) => {
                const defaults: Record<string, string> = { Prowlarr: 'http://localhost:9696', Torznab: 'http://localhost:9117', Newznab: 'http://localhost:5076' };
                setEditing({ ...editing, indexerType: v, url: defaults[v] || editing.url || '' });
              }}
              options={[
                { value: 'Prowlarr', label: 'Prowlarr' },
                { value: 'Torznab', label: 'Torznab' },
                { value: 'Newznab', label: 'Newznab' },
              ]}
            />
            <TextInput label="URL" value={editing.url || ''} onChange={(v) => setEditing({ ...editing, url: v })} placeholder="http://localhost:9696" />
            <TextInput label="API Key" value={editing.apiKey || ''} onChange={(v) => setEditing({ ...editing, apiKey: v })} type="password" />
            <TextInput label="API Path" value={editing.apiPath || '/api'} onChange={(v) => setEditing({ ...editing, apiPath: v })} placeholder="/api" />
            <TextInput label="Categories" value={editing.categories || ''} onChange={(v) => setEditing({ ...editing, categories: v })} placeholder="2000,5000" />
            <Toggle label="RSS" checked={editing.enableRss ?? true} onChange={(v) => setEditing({ ...editing, enableRss: v })} />
            <Toggle label="Search" checked={editing.enableSearch ?? true} onChange={(v) => setEditing({ ...editing, enableSearch: v })} />
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
