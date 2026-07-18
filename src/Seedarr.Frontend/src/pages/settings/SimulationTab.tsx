import { useState, useEffect } from 'react';
import { useSimulationConfig, useSaveSimulationConfig } from '../../api/hooks';
import type { SimulationConfig } from '../../api/types';
import { SaveBar, Toggle, SelectInput, NumberInput, SectionTitle } from './shared';

export function SimulationTab() {
  const { data: config, isLoading } = useSimulationConfig();
  const save = useSaveSimulationConfig();
  const [form, setForm] = useState<SimulationConfig>({
    id: 1,
    clientBehaviorEngineEnabled: true,
    primaryClient: 'qBittorrent',
    behaviorVariation: 0.3,
    clientProfileSwitching: true,
    switchClientProbability: 0.05,
    trafficPatternProfile: 'balanced',
    realisticVariations: true,
    timeBasedPatterns: true,
    swarmIntelligenceEnabled: true,
    swarmAdaptationRate: 0.5,
    swarmPeerAnalysisDepth: 10,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof SimulationConfig>(key: K, value: SimulationConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

      <SectionTitle>Behavior Engine</SectionTitle>
      <Toggle label="Enabled" checked={form.clientBehaviorEngineEnabled} onChange={(v) => set('clientBehaviorEngineEnabled', v)} />
      <SelectInput
        label="Primary Client"
        value={form.primaryClient}
        onChange={(v) => set('primaryClient', v)}
        options={[
          { value: 'qBittorrent', label: 'qBittorrent' },
          { value: 'Deluge', label: 'Deluge' },
          { value: 'Transmission', label: 'Transmission' },
          { value: 'uTorrent', label: 'uTorrent' },
          { value: 'BiglyBT', label: 'BiglyBT' },
        ]}
        disabled={!form.clientBehaviorEngineEnabled}
      />
      <NumberInput label="Behavior Variation" value={form.behaviorVariation} onChange={(v) => set('behaviorVariation', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" disabled={!form.clientBehaviorEngineEnabled} />

      <SectionTitle>Profile Switching</SectionTitle>
      <Toggle label="Client Switching" checked={form.clientProfileSwitching} onChange={(v) => set('clientProfileSwitching', v)} hint="Rotate client identity" />
      <NumberInput label="Switch Probability" value={form.switchClientProbability} onChange={(v) => set('switchClientProbability', v)} min={0} max={1} step={0.01} suffix="/ announce" hint="0.0 - 1.0" disabled={!form.clientProfileSwitching} />

      <SectionTitle>Traffic Patterns</SectionTitle>
      <SelectInput
        label="Profile"
        value={form.trafficPatternProfile}
        onChange={(v) => set('trafficPatternProfile', v)}
        options={[
          { value: 'conservative', label: 'Conservative' },
          { value: 'balanced', label: 'Balanced' },
          { value: 'aggressive', label: 'Aggressive' },
        ]}
      />
      <Toggle label="Realistic Variations" checked={form.realisticVariations} onChange={(v) => set('realisticVariations', v)} />
      <Toggle label="Time-Based Patterns" checked={form.timeBasedPatterns} onChange={(v) => set('timeBasedPatterns', v)} hint="Vary by time of day" />

      <SectionTitle>Swarm Intelligence</SectionTitle>
      <Toggle label="Enabled" checked={form.swarmIntelligenceEnabled} onChange={(v) => set('swarmIntelligenceEnabled', v)} />
      <NumberInput label="Adaptation Rate" value={form.swarmAdaptationRate} onChange={(v) => set('swarmAdaptationRate', v)} min={0} max={1} step={0.1} hint="0.0 - 1.0" disabled={!form.swarmIntelligenceEnabled} />
      <NumberInput label="Peer Analysis Depth" value={form.swarmPeerAnalysisDepth} onChange={(v) => set('swarmPeerAnalysisDepth', v)} min={1} disabled={!form.swarmIntelligenceEnabled} />

      </div>
    </div>
  );
}
