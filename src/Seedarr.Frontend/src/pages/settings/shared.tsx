import React, { useState, useEffect, useCallback, useRef } from "react";
import { useBlocker, type BlockerFunction } from "react-router";
import { usePermissions } from "../../hooks/usePermissions";

export function SaveFeedback({
  isPending: _isPending,
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
          style={{
            marginLeft: "0.75rem",
            fontSize: "0.85rem",
            color: "var(--danger)",
          }}
        >
          Failed to save: {error?.message}
        </span>
      )}
      {isSuccess && !dirty && (
        <span
          style={{
            marginLeft: "0.75rem",
            fontSize: "0.85rem",
            color: "var(--success, #27ae60)",
            fontWeight: 600,
          }}
        >
          ✓ Changes Saved Successfully
        </span>
      )}
    </>
  );
}

export function PendingChangesModal({
  onSave,
  onDiscard,
  onCancel,
  isPending = false,
}: {
  onSave: () => void;
  onDiscard: () => void;
  onCancel: () => void;
  isPending?: boolean;
}) {
  return (
    <div
      className="modal-overlay"
      onClick={isPending ? undefined : onCancel}
    >
      <div
        className="modal"
        onClick={(e) => e.stopPropagation()}
        style={{
          maxWidth: 420,
          borderRadius: "8px",
          boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
          border: "1px solid var(--border-light)",
        }}
      >
        <h2 style={{ margin: "0 0 0.75rem", fontSize: "1.2rem" }}>
          Unsaved Changes
        </h2>
        <p
          style={{
            margin: "0 0 1.25rem",
            color: "var(--text-muted)",
            fontSize: "0.9rem",
            lineHeight: 1.4,
          }}
        >
          You have unsaved changes in this settings section. What would you like
          to do?
        </p>
        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
          <button
            className="btn btn-outline btn-small"
            onClick={onCancel}
            disabled={isPending}
            type="button"
          >
            Stay on Page
          </button>
          <button
            className="btn btn-danger btn-small"
            onClick={onDiscard}
            disabled={isPending}
            type="button"
          >
            Discard Changes
          </button>
          <button
            className="btn btn-primary btn-small"
            onClick={onSave}
            disabled={isPending}
            type="button"
          >
            {isPending ? "Saving..." : "Save and Proceed"}
          </button>
        </div>
      </div>
    </div>
  );
}

export function useUnsavedGuard(dirty: boolean) {
  const shouldBlock = useCallback<BlockerFunction>(
    ({ currentLocation, nextLocation }) =>
      Boolean(
        dirty &&
          (currentLocation.pathname !== nextLocation.pathname ||
            currentLocation.search !== nextLocation.search),
      ),
    [dirty],
  );
  const blocker = useBlocker(shouldBlock);

  useEffect(() => {
    if (!dirty) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty]);

  return blocker;
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
  onSave: () => void | Promise<void>;
}) {
  const { canSaveSettings } = usePermissions();
  const blocker = useUnsavedGuard(dirty);
  const [savingToProceed, setSavingToProceed] = useState(false);

  useEffect(() => {
    if (savingToProceed) {
      if (isError) {
        setSavingToProceed(false);
      } else if (isSuccess && !isPending && blocker.state === "blocked") {
        setSavingToProceed(false);
        blocker.proceed();
      }
    }
  }, [savingToProceed, isSuccess, isPending, isError, blocker]);

  if (!canSaveSettings) {
    return (
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          padding: "0.75rem 1.25rem",
          marginBottom: "1.25rem",
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          border: "1px solid rgba(239, 68, 68, 0.3)",
          backgroundColor: "var(--bg-secondary)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <button
            className="btn btn-outline"
            disabled
            style={{ minWidth: "120px", opacity: 0.5, cursor: "not-allowed" }}
            title="Read-only access: settings cannot be modified"
          >
            🔒 Read Only
          </button>
          <span style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
            Settings cannot be saved with ReadOnly permissions.
          </span>
        </div>
      </div>
    );
  }

  const handleSaveAndProceed = async () => {
    setSavingToProceed(true);
    try {
      const result = onSave();
      if (result && typeof (result as Promise<unknown>).then === "function") {
        await result;
        if (blocker.state === "blocked") {
          setSavingToProceed(false);
          blocker.proceed();
        }
      }
    } catch {
      setSavingToProceed(false);
    }
  };

  const handleDiscard = () => {
    setSavingToProceed(false);
    if (blocker.state === "blocked") {
      blocker.proceed();
    }
  };

  const handleCancel = () => {
    setSavingToProceed(false);
    if (blocker.state === "blocked") {
      blocker.reset();
    }
  };

  const isSaving = isPending || savingToProceed;

  return (
    <>
      <div
        className="card"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "1rem",
          padding: "0.75rem 1.25rem",
          marginBottom: "1.25rem",
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          border: dirty
            ? "1px solid rgba(200, 168, 78, 0.5)"
            : "1px solid var(--border-light)",
          backgroundColor: "var(--bg-secondary)",
          transition: "all 0.2s ease",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.75rem",
            flexWrap: "wrap",
          }}
        >
          <button
            className={`btn ${dirty ? "btn-primary" : "btn-outline"}`}
            onClick={onSave}
            disabled={!dirty || isSaving}
            style={{ minWidth: "120px" }}
          >
            {isSaving
              ? "Saving Changes..."
              : dirty
                ? "💾 Save Changes"
                : "✓ No Changes"}
          </button>
          <SaveFeedback
            isPending={isSaving}
            isError={isError}
            isSuccess={isSuccess}
            error={error}
            dirty={dirty}
          />
        </div>
        {dirty && (
          <span
            className="badge badge-warning"
            style={{ fontSize: "0.75rem", padding: "0.25rem 0.6rem" }}
          >
            ● Unsaved Changes
          </span>
        )}
      </div>
      {blocker.state === "blocked" && (
        <PendingChangesModal
          onSave={handleSaveAndProceed}
          onDiscard={handleDiscard}
          onCancel={handleCancel}
          isPending={isSaving}
        />
      )}
    </>
  );
}

export function SectionCard({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <div
      className="card"
      style={{
        padding: "1.25rem",
        marginBottom: "1.25rem",
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        border: "1px solid var(--border-light)",
      }}
    >
      <div
        style={{
          marginBottom: "1.25rem",
          paddingBottom: "0.6rem",
          borderBottom: "1px solid var(--border-light)",
        }}
      >
        <h3
          style={{
            margin: 0,
            fontSize: "1.1rem",
            color: "var(--text-primary)",
            fontWeight: 600,
          }}
        >
          {title}
        </h3>
        {description && (
          <div
            style={{
              fontSize: "0.8rem",
              color: "var(--text-muted)",
              marginTop: "0.25rem",
            }}
          >
            {description}
          </div>
        )}
      </div>
      {children}
    </div>
  );
}

export function clampNumber(
  val: number,
  min?: number,
  max?: number,
): number {
  if (min !== undefined && val < min) return min;
  if (max !== undefined && val > max) return max;
  return val;
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
  defaultValue,
  placeholder,
  onBlur,
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
  defaultValue?: number;
  placeholder?: string;
  onBlur?: (e: React.FocusEvent<HTMLInputElement>) => void;
}) {
  const isDecimal = Boolean(step && step < 1);
  const initialText =
    value !== undefined && value !== null && !isNaN(value) ? String(value) : "";
  const [text, setText] = useState<string>(initialText);
  const textRef = useRef<string>(initialText);
  const lastValueRef = useRef<number>(value);

  useEffect(() => {
    if (value !== lastValueRef.current) {
      lastValueRef.current = value;
      const strVal =
        value !== undefined && value !== null && !isNaN(value)
          ? String(value)
          : "";
      textRef.current = strVal;
      setText(strVal);
    }
  }, [value]);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const nextText = e.target.value;
    textRef.current = nextText;
    setText(nextText);

    const trimmed = nextText.trim();
    if (trimmed === "" || trimmed === "-" || trimmed.endsWith(".")) {
      return;
    }

    const parsed = isDecimal ? parseFloat(trimmed) : parseInt(trimmed, 10);
    if (isNaN(parsed) || isNaN(Number(trimmed))) {
      return;
    }

    const isWithinBounds =
      (min === undefined || parsed >= min) &&
      (max === undefined || parsed <= max);

    if (isWithinBounds) {
      const normalized = Object.is(parsed, -0) ? 0 : parsed;
      lastValueRef.current = normalized;
      onChange(normalized);
    }
  };

  const handleBlur = (e: React.FocusEvent<HTMLInputElement>) => {
    const trimmed = textRef.current.trim();
    let finalVal: number;

    if (trimmed === "" || trimmed === "-") {
      const fallback = defaultValue !== undefined ? defaultValue : (min ?? 0);
      finalVal = clampNumber(fallback, min, max);
    } else {
      const parsed = isDecimal ? parseFloat(trimmed) : parseInt(trimmed, 10);
      if (isNaN(parsed) || isNaN(Number(trimmed))) {
        const fallback = defaultValue !== undefined ? defaultValue : (min ?? 0);
        finalVal = clampNumber(fallback, min, max);
      } else {
        finalVal = clampNumber(parsed, min, max);
      }
    }

    const normalized = Object.is(finalVal, -0) ? 0 : finalVal;
    lastValueRef.current = normalized;
    const strVal = String(normalized);
    textRef.current = strVal;
    setText(strVal);
    onChange(normalized);
    onBlur?.(e);
  };

  const inputEl = (
    <input
      type="number"
      className="form-input"
      value={text}
      onChange={handleChange}
      onBlur={handleBlur}
      min={min}
      max={max}
      step={step}
      disabled={disabled}
      placeholder={placeholder}
      style={{ borderRadius: suffix ? "6px 0 0 6px" : "6px" }}
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
  rightElement,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  hint?: string;
  disabled?: boolean;
  type?: string;
  rightElement?: React.ReactNode;
}) {
  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        {rightElement ? (
          <div
            style={{
              display: "flex",
              gap: "0.5rem",
              alignItems: "center",
              width: "100%",
            }}
          >
            <input
              type={type || "text"}
              className="form-input"
              value={value ?? ""}
              onChange={(e) => onChange(e.target.value)}
              placeholder={placeholder}
              disabled={disabled}
              style={{ borderRadius: "6px", flex: 1, minWidth: 0 }}
            />
            <div style={{ flexShrink: 0 }}>{rightElement}</div>
          </div>
        ) : (
          <input
            type={type || "text"}
            className="form-input"
            value={value ?? ""}
            onChange={(e) => onChange(e.target.value)}
            placeholder={placeholder}
            disabled={disabled}
            style={{ borderRadius: "6px" }}
          />
        )}
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
  disabled,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  hint?: string;
  disabled?: boolean;
}) {
  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <div className="form-toggle-row">
          <label
            className="toggle-switch"
            style={
              disabled ? { opacity: 0.6, cursor: "not-allowed" } : undefined
            }
          >
            <input
              type="checkbox"
              checked={checked}
              disabled={disabled}
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
          style={{ borderRadius: "6px" }}
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
  return (
    <div
      className="form-section-title"
      style={{
        fontSize: "1.05rem",
        fontWeight: 600,
        color: "var(--accent, #c8a84e)",
        padding: "0.75rem 0 0.5rem",
        marginTop: "1rem",
        marginBottom: "0.5rem",
        borderBottom: "1px solid var(--border-light)",
      }}
    >
      {children}
    </div>
  );
}
