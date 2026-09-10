import { useState, useRef, useEffect } from "react";
import { useTranslation } from "../i18n";
import { LocaleCode } from "../i18n/types";

export function LanguageSelector() {
  const { locale, setLocale, currentLanguage, languages, t } = useTranslation();
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (
        containerRef.current &&
        !containerRef.current.contains(event.target as Node)
      ) {
        setIsOpen(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setIsOpen(false);
      }
    }

    if (isOpen) {
      document.addEventListener("mousedown", handleClickOutside);
      document.addEventListener("keydown", handleKeyDown);
    }
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen]);

  const handleSelectLanguage = (code: LocaleCode) => {
    setLocale(code);
    setIsOpen(false);
  };

  return (
    <div
      ref={containerRef}
      className="language-selector-container"
      style={{ position: "relative", display: "inline-block" }}
    >
      <button
        type="button"
        className="topbar-btn language-selector-btn"
        onClick={() => setIsOpen((prev) => !prev)}
        title={t("topbar.language", undefined, "Language")}
        aria-label={t("topbar.selectLanguage", undefined, "Select Language")}
        aria-expanded={isOpen}
        style={{
          display: "flex",
          alignItems: "center",
          gap: "0.35rem",
          padding: "0.25rem 0.5rem",
          borderRadius: "4px",
          cursor: "pointer",
          fontSize: "0.85rem",
        }}
      >
        <span style={{ fontSize: "1rem" }}>{currentLanguage.flag}</span>
        <span
          className="language-code-text"
          style={{
            fontSize: "0.75rem",
            fontWeight: 500,
            textTransform: "uppercase",
            color: "var(--text-muted)",
          }}
        >
          {currentLanguage.code}
        </span>
      </button>

      {isOpen && (
        <div
          className="topbar-dropdown language-dropdown"
          style={{
            position: "absolute",
            top: "calc(100% + 4px)",
            right: 0,
            minWidth: "220px",
            maxHeight: "340px",
            overflowY: "auto",
            zIndex: 1000,
          }}
          role="menu"
        >
          <div
            style={{
              padding: "6px 12px",
              fontSize: "0.72rem",
              textTransform: "uppercase",
              letterSpacing: "0.05em",
              color: "var(--text-dim)",
              borderBottom: "1px solid var(--border-light, #3a352e)",
            }}
          >
            {t("topbar.selectLanguage", undefined, "Select Language")}
          </div>
          {languages.map((lang) => {
            const isSelected = lang.code === locale;
            return (
              <button
                key={lang.code}
                type="button"
                className={`topbar-dropdown-item language-dropdown-item ${
                  isSelected ? "selected" : ""
                }`}
                onClick={() => handleSelectLanguage(lang.code)}
                role="menuitem"
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  width: "100%",
                  padding: "6px 12px",
                  fontSize: "0.82rem",
                  backgroundColor: isSelected
                    ? "var(--bg-hover, rgba(200, 168, 78, 0.15))"
                    : "transparent",
                  fontWeight: isSelected ? 600 : 400,
                  color: isSelected
                    ? "var(--accent, #e5a00d)"
                    : "var(--color-text, #c8b89a)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "0.5rem",
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                  }}
                >
                  <span style={{ fontSize: "1rem" }}>{lang.flag}</span>
                  <span style={{ direction: lang.rtl ? "rtl" : "ltr" }}>
                    {lang.nativeName}
                  </span>
                  {lang.name !== lang.nativeName && (
                    <span
                      style={{
                        fontSize: "0.75rem",
                        color: "var(--text-dim)",
                      }}
                    >
                      ({lang.name})
                    </span>
                  )}
                </div>
                {isSelected && (
                  <span
                    style={{
                      marginLeft: "0.5rem",
                      color: "var(--accent, #e5a00d)",
                      fontSize: "0.8rem",
                    }}
                  >
                    ✓
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

export default LanguageSelector;
