import { useState, useEffect, useRef } from "react";
import { useTranslation } from "../i18n";
import { useModalRegistration } from "./ModalProvider";
import { useFocusTrap } from "../hooks/useFocusTrap";

export interface PromptModalProps {
  isOpen: boolean;
  title: string;
  description?: string;
  initialValue?: string | number;
  inputType?: "text" | "number";
  placeholder?: string;
  min?: number;
  max?: number;
  step?: number;
  suffix?: string;
  validate?: (val: string) => string | null;
  onConfirm: (val: string) => void;
  onClose: () => void;
  confirmText?: string;
  cancelText?: string;
}

export function PromptModal({
  isOpen,
  title,
  description,
  initialValue = "",
  inputType = "text",
  placeholder = "",
  min,
  max,
  step,
  suffix,
  validate,
  onConfirm,
  onClose,
  confirmText,
  cancelText,
}: PromptModalProps) {
  const { t } = useTranslation();
  const [value, setValue] = useState(String(initialValue ?? ""));
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const modalRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    initialFocusRef: inputRef,
    onEscape: onClose,
  });

  useModalRegistration({
    id: "prompt-modal",
    isOpen,
    onClose,
    modalRef,
  });

  useEffect(() => {
    if (isOpen) {
      setValue(String(initialValue ?? ""));
      setError(null);
      setTimeout(() => {
        inputRef.current?.focus();
        inputRef.current?.select();
      }, 50);
    }
  }, [isOpen, initialValue]);

  if (!isOpen) return null;

  const handleSubmit = (e?: React.FormEvent) => {
    if (e) e.preventDefault();

    if (inputType === "number") {
      const num = Number(value);
      if (isNaN(num)) {
        setError(t("common.invalidNumber", undefined, "Please enter a valid number"));
        return;
      }
      if (min !== undefined && num < min) {
        setError(`Value cannot be less than ${min}`);
        return;
      }
      if (max !== undefined && num > max) {
        setError(`Value cannot be greater than ${max}`);
        return;
      }
    }

    if (validate) {
      const customError = validate(value);
      if (customError) {
        setError(customError);
        return;
      }
    }

    onConfirm(value);
    onClose();
  };

  return (
    <div
      ref={modalRef}
      className="modal-overlay"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-labelledby="prompt-modal-title"
      tabIndex={-1}
      aria-describedby={description ? "prompt-modal-desc" : undefined}
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.75)",
        backdropFilter: "blur(6px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 9999,
        padding: "1rem",
      }}
    >
      <div
        className="modal"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: "100%",
          maxWidth: 440,
          backgroundColor: "var(--bg-card, #161826)",
          borderRadius: "8px",
          padding: "1.5rem",
          boxShadow: "0 16px 40px rgba(0,0,0,0.7)",
          border: "1px solid var(--border-light)",
        }}
      >
        <h3
          id="prompt-modal-title"
          style={{
            margin: "0 0 0.5rem 0",
            fontSize: "1.15rem",
            fontWeight: 600,
            color: "var(--text-primary, #fff)",
          }}
        >
          {title}
        </h3>

        {description && (
          <p
            id="prompt-modal-desc"
            style={{
              margin: "0 0 1rem 0",
              fontSize: "0.85rem",
              color: "var(--text-muted, #94a3b8)",
              lineHeight: 1.4,
            }}
          >
            {description}
          </p>
        )}

        <form onSubmit={handleSubmit}>
          <div style={{ position: "relative", marginBottom: "1rem" }}>
            <input
              ref={inputRef}
              type={inputType}
              value={value}
              onChange={(e) => {
                setValue(e.target.value);
                if (error) setError(null);
              }}
              min={min}
              max={max}
              step={step}
              placeholder={placeholder}
              className="input"
              style={{
                width: "100%",
                padding: suffix ? "0.6rem 3rem 0.6rem 0.75rem" : "0.6rem 0.75rem",
                borderRadius: "6px",
                border: error
                  ? "1px solid var(--danger, #ef4444)"
                  : "1px solid var(--border-light)",
                backgroundColor: "var(--bg-primary, #0f111c)",
                color: "var(--text-primary, #fff)",
                fontSize: "0.9rem",
                outline: "none",
                boxSizing: "border-box",
              }}
            />
            {suffix && (
              <span
                style={{
                  position: "absolute",
                  right: "0.75rem",
                  top: "50%",
                  transform: "translateY(-50%)",
                  color: "var(--text-muted, #64748b)",
                  fontSize: "0.8rem",
                  pointerEvents: "none",
                }}
              >
                {suffix}
              </span>
            )}
          </div>

          {error && (
            <div
              style={{
                marginBottom: "1rem",
                padding: "0.4rem 0.75rem",
                borderRadius: "4px",
                fontSize: "0.8rem",
                backgroundColor: "rgba(239, 68, 68, 0.15)",
                color: "var(--danger, #ef4444)",
                border: "1px solid rgba(239, 68, 68, 0.3)",
              }}
            >
              ✕ {error}
            </div>
          )}

          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: "0.5rem",
            }}
          >
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={onClose}
              style={{ padding: "0.4rem 0.9rem" }}
            >
              {cancelText || t("common.cancel", undefined, "Cancel")}
            </button>
            <button
              type="submit"
              className="btn btn-primary btn-small"
              style={{ padding: "0.4rem 1rem" }}
            >
              {confirmText || t("common.save", undefined, "Save")}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export default PromptModal;
