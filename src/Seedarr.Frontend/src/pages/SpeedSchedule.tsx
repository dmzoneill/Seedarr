import React, { useState } from 'react';
import {
  useSpeedSchedules,
  useActiveSpeedLimits,
  useCreateSpeedSchedule,
  useUpdateSpeedSchedule,
  useDeleteSpeedSchedule,
} from '../api/hooks';
import { formatSpeed } from '../utils/formatters';
import type { SpeedScheduleEntry } from '../api/types';

const DAY_FLAGS = [
  { label: 'Mon', value: 1 },
  { label: 'Tue', value: 2 },
  { label: 'Wed', value: 4 },
  { label: 'Thu', value: 8 },
  { label: 'Fri', value: 16 },
  { label: 'Sat', value: 32 },
  { label: 'Sun', value: 64 },
];

const BLOCK_COLORS = [
  'var(--color-primary, #3498db)',
  'var(--color-success, #27ae60)',
  'var(--color-warning, #f39c12)',
  '#9b59b6',
  '#e67e22',
  '#1abc9c',
];

function daysToLabels(days: number): string {
  if (days === 127) return 'Every day';
  if (days === 31) return 'Weekdays';
  if (days === 96) return 'Weekends';
  return DAY_FLAGS.filter((d) => days & d.value).map((d) => d.label).join(', ');
}

function timeToHour(time: string): number {
  const [h, m] = time.split(':').map(Number);
  return h + m / 60;
}

const EMPTY_SCHEDULE: Omit<SpeedScheduleEntry, 'id'> = {
  name: '',
  days: 127,
  startTime: '00:00',
  endTime: '23:59',
  maxUploadSpeed: 0,
  maxDownloadSpeed: 0,
  isEnabled: true,
  priority: 0,
};

function ScheduleModal({
  schedule,
  onSave,
  onCancel,
  isPending,
}: {
  schedule: Partial<SpeedScheduleEntry>;
  onSave: (s: Partial<SpeedScheduleEntry>) => void;
  onCancel: () => void;
  isPending: boolean;
}) {
  const [form, setForm] = useState({ ...EMPTY_SCHEDULE, ...schedule });

  function toggleDay(value: number) {
    setForm({ ...form, days: form.days ^ value });
  }

  return (
    <div className="modal-overlay" onClick={onCancel}>
      <div className="modal" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 480 }}>
        <h2>{schedule.id ? 'Edit Schedule' : 'Add Schedule'}</h2>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <label>
            <span className="status-label">Name</span>
            <input className="form-input" type="text" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </label>
          <div>
            <span className="status-label">Days</span>
            <div style={{ display: 'flex', gap: 4, marginTop: 4 }}>
              {DAY_FLAGS.map((d) => (
                <button
                  key={d.value}
                  className={`btn btn-sm ${form.days & d.value ? 'btn-primary' : 'btn-default'}`}
                  onClick={() => toggleDay(d.value)}
                  type="button"
                >
                  {d.label}
                </button>
              ))}
            </div>
          </div>
          <div style={{ display: 'flex', gap: 12 }}>
            <label style={{ flex: 1 }}>
              <span className="status-label">Start Time</span>
              <input className="form-input" type="time" value={form.startTime} onChange={(e) => setForm({ ...form, startTime: e.target.value })} />
            </label>
            <label style={{ flex: 1 }}>
              <span className="status-label">End Time</span>
              <input className="form-input" type="time" value={form.endTime} onChange={(e) => setForm({ ...form, endTime: e.target.value })} />
            </label>
          </div>
          <div style={{ display: 'flex', gap: 12 }}>
            <label style={{ flex: 1 }}>
              <span className="status-label">Max Upload (KB/s, 0=global)</span>
              <input className="form-input" type="number" min={0} value={form.maxUploadSpeed / 1024} onChange={(e) => setForm({ ...form, maxUploadSpeed: Math.round(Number(e.target.value) * 1024) })} />
            </label>
            <label style={{ flex: 1 }}>
              <span className="status-label">Max Download (KB/s, 0=global)</span>
              <input className="form-input" type="number" min={0} value={form.maxDownloadSpeed / 1024} onChange={(e) => setForm({ ...form, maxDownloadSpeed: Math.round(Number(e.target.value) * 1024) })} />
            </label>
          </div>
          <div style={{ display: 'flex', gap: 12 }}>
            <label style={{ flex: 1 }}>
              <span className="status-label">Priority</span>
              <input className="form-input" type="number" value={form.priority} onChange={(e) => setForm({ ...form, priority: Number(e.target.value) })} />
            </label>
            <label style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 8, paddingTop: 20 }}>
              <input type="checkbox" checked={form.isEnabled} onChange={(e) => setForm({ ...form, isEnabled: e.target.checked })} />
              <span>Enabled</span>
            </label>
          </div>
          <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', marginTop: 8 }}>
            <button className="btn btn-default" onClick={onCancel}>Cancel</button>
            <button className="btn btn-primary" onClick={() => onSave(form)} disabled={isPending || !form.name.trim()}>
              {isPending ? 'Saving...' : 'Save'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function WeeklyCalendar({ schedules }: { schedules: SpeedScheduleEntry[] }) {
  const hours = Array.from({ length: 24 }, (_, i) => i);

  return (
    <div className="card" style={{ overflowX: 'auto' }}>
      <h3>Weekly View</h3>
      <div style={{ display: 'grid', gridTemplateColumns: '50px repeat(7, 1fr)', gap: 0, minWidth: 600 }}>
        <div />
        {DAY_FLAGS.map((d) => (
          <div key={d.value} style={{ textAlign: 'center', fontWeight: 600, padding: '4px 0', borderBottom: '1px solid var(--color-border, #333)' }}>
            {d.label}
          </div>
        ))}
        {hours.map((hour) => (
          <React.Fragment key={hour}>
            <div style={{ fontSize: 11, color: 'var(--color-text-muted, #888)', textAlign: 'right', paddingRight: 6, paddingTop: 2, borderTop: '1px solid var(--color-border, #333)' }}>
              {String(hour).padStart(2, '0')}:00
            </div>
            {DAY_FLAGS.map((day) => {
              const active = schedules.filter((s) =>
                s.isEnabled && (s.days & day.value) && timeToHour(s.startTime) <= hour && timeToHour(s.endTime) > hour
              );
              const top = active[0];
              return (
                <div
                  key={`${hour}-${day.value}`}
                  style={{
                    height: 20,
                    borderTop: '1px solid var(--color-border, #333)',
                    borderLeft: '1px solid var(--color-border, #333)',
                    backgroundColor: top ? BLOCK_COLORS[schedules.indexOf(top) % BLOCK_COLORS.length] : 'transparent',
                    opacity: top ? 0.7 : 1,
                  }}
                  title={top ? `${top.name}: ${formatSpeed(top.maxUploadSpeed)} up / ${formatSpeed(top.maxDownloadSpeed)} down` : ''}
                />
              );
            })}
          </React.Fragment>
        ))}
      </div>
      {schedules.length > 0 && (
        <div style={{ display: 'flex', gap: 16, marginTop: 12, flexWrap: 'wrap' }}>
          {schedules.map((s, i) => (
            <div key={s.id} style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <div style={{ width: 12, height: 12, borderRadius: 2, backgroundColor: BLOCK_COLORS[i % BLOCK_COLORS.length], opacity: s.isEnabled ? 0.7 : 0.3 }} />
              <span style={{ opacity: s.isEnabled ? 1 : 0.5 }}>{s.name}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function SpeedSchedule() {
  const { data: schedules, isLoading, isError } = useSpeedSchedules();
  const { data: activeLimits } = useActiveSpeedLimits();
  const createSchedule = useCreateSpeedSchedule();
  const updateSchedule = useUpdateSpeedSchedule();
  const deleteSchedule = useDeleteSpeedSchedule();

  const [modal, setModal] = useState<Partial<SpeedScheduleEntry> | null>(null);

  function handleSave(form: Partial<SpeedScheduleEntry>) {
    if (form.id) {
      updateSchedule.mutate(form as SpeedScheduleEntry, { onSuccess: () => setModal(null) });
    } else {
      createSchedule.mutate(form, { onSuccess: () => setModal(null) });
    }
  }

  return (
    <div>
      <div className="page-heading-row">
        <h1 className="page-heading">Speed Schedule</h1>
        <button className="btn btn-primary" onClick={() => setModal({ ...EMPTY_SCHEDULE })}>
          Add Schedule
        </button>
      </div>

      {activeLimits && (
        <div className="card" style={{ marginBottom: 16 }}>
          <div className="status-row">
            <span className="status-label">Active Schedule</span>
            <span className="status-value">
              {activeLimits.isScheduleActive ? (
                <span className="badge badge-seeding">{activeLimits.activeScheduleName}</span>
              ) : (
                <span className="badge badge-stopped">None</span>
              )}
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">Upload Limit</span>
            <span className="status-value">
              {activeLimits.maxUploadSpeed > 0 ? formatSpeed(activeLimits.maxUploadSpeed) : 'Global'}
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">Download Limit</span>
            <span className="status-value">
              {activeLimits.maxDownloadSpeed > 0 ? formatSpeed(activeLimits.maxDownloadSpeed) : 'Global'}
            </span>
          </div>
        </div>
      )}

      {!isLoading && !isError && <WeeklyCalendar schedules={schedules ?? []} />}

      <div className="card">
        <h3>Schedules</h3>
        {isLoading ? (
          <p className="loading">Loading schedules...</p>
        ) : isError ? (
          <p className="error">Failed to load data.</p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Enabled</th>
                  <th className="torrent-table-th">Name</th>
                  <th className="torrent-table-th">Days</th>
                  <th className="torrent-table-th">Time</th>
                  <th className="torrent-table-th">Upload Limit</th>
                  <th className="torrent-table-th">Download Limit</th>
                  <th className="torrent-table-th">Priority</th>
                  <th className="torrent-table-th">Actions</th>
                </tr>
              </thead>
              <tbody>
                {(schedules ?? []).length === 0 ? (
                  <tr>
                    <td colSpan={8} className="torrent-table-empty">No speed schedules configured</td>
                  </tr>
                ) : (
                  (schedules ?? []).map((s) => (
                    <tr key={s.id} className="torrent-table-row">
                      <td>
                        <span className={`badge ${s.isEnabled ? 'badge-seeding' : 'badge-stopped'}`}>
                          {s.isEnabled ? 'On' : 'Off'}
                        </span>
                      </td>
                      <td>{s.name}</td>
                      <td>{daysToLabels(s.days)}</td>
                      <td>{s.startTime} - {s.endTime}</td>
                      <td>{s.maxUploadSpeed > 0 ? formatSpeed(s.maxUploadSpeed) : 'Global'}</td>
                      <td>{s.maxDownloadSpeed > 0 ? formatSpeed(s.maxDownloadSpeed) : 'Global'}</td>
                      <td>{s.priority}</td>
                      <td>
                        <div style={{ display: 'flex', gap: 4 }}>
                          <button className="btn btn-sm btn-default" onClick={() => setModal({ ...s })}>Edit</button>
                          <button className="btn btn-sm btn-danger" onClick={() => deleteSchedule.mutate(s.id)}>Delete</button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {modal && (
        <ScheduleModal
          schedule={modal}
          onSave={handleSave}
          onCancel={() => setModal(null)}
          isPending={createSchedule.isPending || updateSchedule.isPending}
        />
      )}
    </div>
  );
}

export default SpeedSchedule;
