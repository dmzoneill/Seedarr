import { useState, useEffect } from 'react';
import { useSeedingConfig, useSaveSeedingConfig } from '../../api/hooks';
import type { SeedingConfig } from '../../api/types';
import { SaveBar, Toggle, SelectInput, NumberInput, SectionTitle } from './shared';

export function SeedingTab() {
  const { data: config, isLoading } = useSeedingConfig();
  const save = useSaveSeedingConfig();
  const [form, setForm] = useState<SeedingConfig>({
    id: 1,
    maxUploadSpeedKbps: 625,
    maxDownloadSpeedKbps: 1250,
    alternativeSpeedEnabled: false,
    altUploadSpeedKbps: 50,
    altDownloadSpeedKbps: 100,
    globalSeedRatioLimit: 0,
    speedVariationMin: 0.2,
    speedVariationMax: 0.8,
    uploadDistributionAlgorithm: 'Equal',
    uploadDistributionSpreadPercentage: 50,
    uploadRedistributionMode: 'tick',
    uploadCustomIntervalMinutes: 5,
    uploadStoppedMinPercentage: 20,
    uploadStoppedMaxPercentage: 40,
    downloadDistributionAlgorithm: 'Equal',
    downloadDistributionSpreadPercentage: 50,
    downloadRedistributionMode: 'tick',
    downloadCustomIntervalMinutes: 5,
    downloadStoppedMinPercentage: 20,
    downloadStoppedMaxPercentage: 40,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof SeedingConfig>(key: K, value: SeedingConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  const distOptions = [
    { value: 'Equal', label: 'Equal' },
    { value: 'Pareto', label: 'Pareto' },
    { value: 'PowerLaw', label: 'Power Law' },
    { value: 'LogNormal', label: 'Log Normal' },
  ];

  const redistOptions = [
    { value: 'tick', label: 'Every Tick' },
    { value: 'interval', label: 'Custom Interval' },
    { value: 'fixed', label: 'Fixed' },
  ];

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

      <SectionTitle>Quick Profiles</SectionTitle>
      <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem' }}>
        <button
          className="btn"
          onClick={() => {
            setForm((prev) => ({
              ...prev,
              maxUploadSpeedKbps: 100,
              maxDownloadSpeedKbps: 200,
              uploadDistributionAlgorithm: 'Equal',
              uploadDistributionSpreadPercentage: 30,
              downloadDistributionAlgorithm: 'Equal',
              downloadDistributionSpreadPercentage: 30,
              globalSeedRatioLimit: 1.5,
            }));
            setDirty(true);
          }}
        >
          Conservative
        </button>
        <button
          className="btn"
          onClick={() => {
            setForm((prev) => ({
              ...prev,
              maxUploadSpeedKbps: 625,
              maxDownloadSpeedKbps: 1250,
              uploadDistributionAlgorithm: 'Pareto',
              uploadDistributionSpreadPercentage: 50,
              downloadDistributionAlgorithm: 'Pareto',
              downloadDistributionSpreadPercentage: 50,
              globalSeedRatioLimit: 2.0,
            }));
            setDirty(true);
          }}
        >
          Balanced
        </button>
        <button
          className="btn"
          onClick={() => {
            setForm((prev) => ({
              ...prev,
              maxUploadSpeedKbps: 2500,
              maxDownloadSpeedKbps: 5000,
              uploadDistributionAlgorithm: 'PowerLaw',
              uploadDistributionSpreadPercentage: 80,
              downloadDistributionAlgorithm: 'PowerLaw',
              downloadDistributionSpreadPercentage: 80,
              globalSeedRatioLimit: 0,
            }));
            setDirty(true);
          }}
        >
          Aggressive
        </button>
      </div>
      <div className="form-hint" style={{ marginBottom: '1rem', fontSize: '0.8rem', color: 'var(--text-muted)' }}>
        Conservative: low speeds, equal distribution, 1.5 ratio limit.
        Balanced: default speeds (5/10 Mbit), Pareto distribution, 2.0 ratio limit.
        Aggressive: high speed caps (20/40 Mbit), power law distribution, no ratio limit.
      </div>

      <SectionTitle>Speed Limits</SectionTitle>
      <NumberInput label="Max Upload Speed" value={form.maxUploadSpeedKbps} onChange={(v) => set('maxUploadSpeedKbps', v)} min={1} suffix="KB/s" hint="Default 625 KB/s (5 Mbit/s). Minimum 1 KB/s." />
      <NumberInput label="Max Download Speed" value={form.maxDownloadSpeedKbps} onChange={(v) => set('maxDownloadSpeedKbps', v)} min={1} suffix="KB/s" hint="Default 1250 KB/s (10 Mbit/s). Minimum 1 KB/s." />
      <NumberInput label="Global Seed Ratio Limit" value={form.globalSeedRatioLimit} onChange={(v) => set('globalSeedRatioLimit', v)} min={0} step={0.1} hint="Stop seeding when ratio reaches this value. Set to 0 to disable." />

      <SectionTitle>Alternative Speeds</SectionTitle>
      <Toggle label="Enable Alt Speeds" checked={form.alternativeSpeedEnabled} onChange={(v) => set('alternativeSpeedEnabled', v)} />
      <NumberInput label="Alt Upload Speed" value={form.altUploadSpeedKbps} onChange={(v) => set('altUploadSpeedKbps', v)} min={1} suffix="KB/s" disabled={!form.alternativeSpeedEnabled} />
      <NumberInput label="Alt Download Speed" value={form.altDownloadSpeedKbps} onChange={(v) => set('altDownloadSpeedKbps', v)} min={1} suffix="KB/s" disabled={!form.alternativeSpeedEnabled} />

      <SectionTitle>Upload Distribution</SectionTitle>
      <SelectInput label="Algorithm" value={form.uploadDistributionAlgorithm} onChange={(v) => set('uploadDistributionAlgorithm', v)} options={distOptions} />
      <NumberInput label="Spread %" value={form.uploadDistributionSpreadPercentage} onChange={(v) => set('uploadDistributionSpreadPercentage', v)} min={0} max={100} />
      <SelectInput label="Redistribution" value={form.uploadRedistributionMode} onChange={(v) => set('uploadRedistributionMode', v)} options={redistOptions} />
      <NumberInput label="Custom Interval" value={form.uploadCustomIntervalMinutes} onChange={(v) => set('uploadCustomIntervalMinutes', v)} min={1} suffix="minutes" disabled={form.uploadRedistributionMode !== 'interval'} />
      <NumberInput label="Stopped Min %" value={form.uploadStoppedMinPercentage} onChange={(v) => set('uploadStoppedMinPercentage', v)} min={0} max={100} />
      <NumberInput label="Stopped Max %" value={form.uploadStoppedMaxPercentage} onChange={(v) => set('uploadStoppedMaxPercentage', v)} min={0} max={100} />

      <SectionTitle>Download Distribution</SectionTitle>
      <SelectInput label="Algorithm" value={form.downloadDistributionAlgorithm} onChange={(v) => set('downloadDistributionAlgorithm', v)} options={distOptions} />
      <NumberInput label="Spread %" value={form.downloadDistributionSpreadPercentage} onChange={(v) => set('downloadDistributionSpreadPercentage', v)} min={0} max={100} />
      <SelectInput label="Redistribution" value={form.downloadRedistributionMode} onChange={(v) => set('downloadRedistributionMode', v)} options={redistOptions} />
      <NumberInput label="Custom Interval" value={form.downloadCustomIntervalMinutes} onChange={(v) => set('downloadCustomIntervalMinutes', v)} min={1} suffix="minutes" disabled={form.downloadRedistributionMode !== 'interval'} />
      <NumberInput label="Stopped Min %" value={form.downloadStoppedMinPercentage} onChange={(v) => set('downloadStoppedMinPercentage', v)} min={0} max={100} />
      <NumberInput label="Stopped Max %" value={form.downloadStoppedMaxPercentage} onChange={(v) => set('downloadStoppedMaxPercentage', v)} min={0} max={100} />

      </div>
    </div>
  );
}
