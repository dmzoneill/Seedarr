import { useState, useEffect, useRef } from "react";
import { useLocation } from "react-router";

export function SaveFeedback({
  isPending,
  isError,
  isSuccess,
  error,
  dirty,
}: {
  isPending: boolean;
  isError: boolean;
  isSuccess: boolean;
  error: Error | null;
  dirty: boolean;
}) {
  return (
    <>
      {isError && (
        <span
          className="error"
          style={{ marginLeft: "0.75rem", fontSize: "0.85rem" }}
        >
          Failed to save: {error?.message}
        </span>
      )}
      {isSuccess && !dirty && (
        <span
          style={{
            marginLeft: "0.75rem",
            fontSize: "0.85rem",
            color: "var(--success)",
          }}
        >
          Saved
        </span>
      )}
    </>
  );
}

export function PendingChangesModal({
  onSave,
  onDiscard,
  onCancel,
}: {
  onSave: () => void;
  onDiscard: () => void;
  onCancel: () => void;
}) {
  return (
    <div className="modal-overlay" onClick={onCancel}>
      <div
        className="modal"
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: 400 }}
      >
        <h2>Unsaved Changes</h2>
        <p
          style={{
            margin: "12px 0 20px",
            color: "var(--color-text-muted, #aaa)",
          }}
        >
          You have unsaved changes. What would you like to do?
        </p>
        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
          <button className="btn btn-default" onClick={onCancel}>
            Stay
          </button>
          <button className="btn btn-danger" onClick={onDiscard}>
            Discard
          </button>
          <button className="btn btn-success" onClick={onSave}>
            Save
          </button>
        </div>
      </div>
    </div>
  );
}

export function useUnsavedGuard(dirty: boolean) {
  const location = useLocation();
  const [pendingNav, setPendingNav] = useState(false);
  const prevPathRef = useRef(location.pathname);

  useEffect(() => {
    if (!dirty) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty]);

  useEffect(() => {
    if (dirty && location.pathname !== prevPathRef.current) {
      setPendingNav(true);
    }
  }, [dirty, location.pathname]);

  return {
    blocked: pendingNav,
    dismiss: () => setPendingNav(false),
  };
}

export function SaveBar({
  dirty,
  isPending,
  isError,
  isSuccess,
  error,
  onSave,
}: {
  dirty: boolean;
  isPending: boolean;
  isError: boolean;
  isSuccess: boolean;
  error: Error | null;
  onSave: () => void;
}) {
  const guard = useUnsavedGuard(dirty);

  return (
    <>
      <div className="settings-toolbar">
        <button
          className="btn btn-success"
          onClick={onSave}
          disabled={!dirty || isPending}
        >
          {isPending ? "Saving..." : dirty ? "Save Changes" : "No Changes"}
        </button>
        <SaveFeedback
          isPending={isPending}
          isError={isError}
          isSuccess={isSuccess}
          error={error}
          dirty={dirty}
        />
      </div>
      {guard.blocked && (
        <PendingChangesModal
          onSave={() => {
            onSave();
            guard.dismiss();
          }}
          onDiscard={guard.dismiss}
          onCancel={guard.dismiss}
        />
      )}
    </>
  );
}

export function NumberInput({
  label,
  value,
  onChange,
  min,
  max,
  step,
  hint,
  suffix,
  disabled,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  min?: number;
  max?: number;
  step?: number;
  hint?: string;
  suffix?: string;
  disabled?: boolean;
}) {
  const inputEl = (
    <input
      type="number"
      className="form-input"
      value={value}
      onChange={(e) =>
        onChange(
          step && step < 1
            ? parseFloat(e.target.value) || 0
            : parseInt(e.target.value, 10) || 0,
        )
      }
      min={min}
      max={max}
      step={step}
      disabled={disabled}
    />
  );

  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        {suffix ? (
          <div className="form-input-with-suffix">
            {inputEl}
            <span className="form-input-suffix">{suffix}</span>
          </div>
        ) : (
          inputEl
        )}
        {hint && <span className="form-hint">{hint}</span>}
      </div>
    </div>
  );
}

export function TextInput({
  label,
  value,
  onChange,
  placeholder,
  hint,
  disabled,
  type,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  hint?: string;
  disabled?: boolean;
  type?: string;
}) {
  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <input
          type={type || "text"}
          className="form-input"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          disabled={disabled}
        />
        {hint && <span className="form-hint">{hint}</span>}
      </div>
    </div>
  );
}

export function Toggle({
  label,
  checked,
  onChange,
  hint,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  hint?: string;
}) {
  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <div className="form-toggle-row">
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={checked}
              onChange={(e) => onChange(e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
          {hint && <span className="form-toggle-description">{hint}</span>}
        </div>
      </div>
    </div>
  );
}

export function SelectInput({
  label,
  value,
  onChange,
  options,
  hint,
  disabled,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
  hint?: string;
  disabled?: boolean;
}) {
  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <select
          className="form-select"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled}
        >
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        {hint && <span className="form-hint">{hint}</span>}
      </div>
    </div>
  );
}

export function SectionTitle({ children }: { children: React.ReactNode }) {
  return <div className="form-section-title">{children}</div>;
}
