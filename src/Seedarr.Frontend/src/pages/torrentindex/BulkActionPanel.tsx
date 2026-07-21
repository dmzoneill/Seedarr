import { PlayIcon, StopIcon } from '../../components/icons/UIIcons';

interface BulkActionPanelProps {
  selectedIds: Set<number>;
  isPending?: boolean;
  onStart: () => void;
  onStop: () => void;
  onDelete: () => void;
  onClear: () => void;
}

export function BulkActionPanel({ selectedIds, isPending = false, onStart, onStop, onDelete, onClear }: BulkActionPanelProps) {
  return (
    <div className="card" style={{ position: 'sticky', bottom: 0, zIndex: 10, display: 'flex', alignItems: 'center', gap: 8, padding: '8px 16px', margin: '8px 0 0' }}>
      <span style={{ fontWeight: 600 }}>{selectedIds.size} selected</span>
      <button className="btn btn-success btn-sm" onClick={onStart} disabled={isPending}>
        <PlayIcon size={12} /> Start
      </button>
      <button className="btn btn-danger btn-sm" onClick={onStop} disabled={isPending}>
        <StopIcon size={12} /> Stop
      </button>
      <button className="btn btn-danger btn-sm" onClick={onDelete} disabled={isPending}>
        Delete
      </button>
      <button className="btn btn-default btn-sm" onClick={onClear} disabled={isPending}>
        Clear
      </button>
    </div>
  );
}
