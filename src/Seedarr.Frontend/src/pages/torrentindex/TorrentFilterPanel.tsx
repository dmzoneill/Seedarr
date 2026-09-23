import { useState } from "react";
import {
  AllIcon,
  SeedingIcon,
  StoppedIcon,
  QueuedIcon,
  ErrorIcon,
  ChevronsLeftIcon,
} from "../../components/icons/UIIcons";
import TrackerFavicon from "../../components/TrackerFavicon";
import { useTranslation } from "../../i18n";
import type { TagGroupItem } from "./useTorrentIndexState";

export type TagGroupInput = TagGroupItem | [string, number];

const STATE_FILTERS = ["All", "Seeding", "Stopped", "Queued", "Error"] as const;

const STATE_FILTER_ICONS: Record<string, React.ReactNode> = {
  All: <AllIcon size={13} />,
  Seeding: <SeedingIcon size={13} />,
  Stopped: <StoppedIcon size={13} />,
  Queued: <QueuedIcon size={13} />,
  Error: <ErrorIcon size={13} />,
};

interface TorrentFilterPanelProps {
  selectedState: string;
  onSelectState: (state: string) => void;
  selectedTracker: string;
  onSelectTracker: (tracker: string) => void;
  selectedCategory?: string;
  onSelectCategory?: (category: string) => void;
  selectedTag?: string;
  onSelectTag?: (tag: string) => void;
  selectedTagIds?: Set<number>;
  onToggleTag?: (tagId: number) => void;
  tagMatchMode?: "AND" | "OR";
  onTagMatchModeChange?: (mode: "AND" | "OR") => void;
  onClearTags?: () => void;
  untaggedCount?: number;
  stateCounts: Record<string, number>;
  trackerGroups: [string, number][];
  categoryGroups?: [string, number][];
  tagGroups?: TagGroupInput[];
  count: number;
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
}

export function TorrentFilterPanel({
  selectedState,
  onSelectState,
  selectedTracker,
  onSelectTracker,
  selectedCategory = "All",
  onSelectCategory,
  selectedTag = "All",
  onSelectTag,
  selectedTagIds,
  onToggleTag,
  tagMatchMode = "OR",
  onTagMatchModeChange,
  onClearTags,
  untaggedCount,
  stateCounts,
  trackerGroups,
  categoryGroups = [],
  tagGroups = [],
  count,
  isCollapsed = false,
  onToggleCollapse,
}: TorrentFilterPanelProps) {
  const normalizedTagGroups: TagGroupItem[] = (tagGroups ?? []).map(
    (tg, idx) => {
      if (Array.isArray(tg)) {
        return { id: idx + 1000, label: tg[0], count: tg[1] };
      }
      return tg;
    },
  );
  const { t } = useTranslation();

  const [isStateOpen, setIsStateOpen] = useState(true);
  const [isTrackerOpen, setIsTrackerOpen] = useState(true);
  const [isCategoryOpen, setIsCategoryOpen] = useState(true);
  const [isTagOpen, setIsTagOpen] = useState(true);

  return (
    <div className={`filter-panel ${isCollapsed ? "collapsed" : ""}`}>
      {/* State Filter Section */}
      <div
        className="filter-panel-header"
        style={{ cursor: "pointer", userSelect: "none" }}
        onClick={() => setIsStateOpen(!isStateOpen)}
      >
        <div
          className="filter-panel-section"
          style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
        >
          <span style={{ fontSize: "0.7rem" }}>{isStateOpen ? "▼" : "▶"}</span>
          <span>{t("torrents.filterState", undefined, "State")}</span>
        </div>
        {onToggleCollapse && (
          <button
            type="button"
            className="filter-panel-collapse-btn"
            onClick={(e) => {
              e.stopPropagation();
              onToggleCollapse();
            }}
            title="Collapse filter sidebar"
            aria-label="Collapse filter sidebar"
          >
            <ChevronsLeftIcon size={13} />
          </button>
        )}
      </div>
      {isStateOpen && (
        <ul className="filter-panel-list">
          {STATE_FILTERS.map((state) => (
            <li key={state}>
              <button
                className={`filter-panel-item${selectedState === state ? " active" : ""}`}
                onClick={() => onSelectState(state)}
              >
                <span className="filter-panel-label">
                  {STATE_FILTER_ICONS[state]}{" "}
                  {state === "All"
                    ? t("common.all", undefined, "All")
                    : t(`torrents.${state.toLowerCase()}`, undefined, state)}
                </span>
                <span className="filter-panel-count">
                  {stateCounts[state] ?? 0}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {/* Tracker Filter Section */}
      <div
        className="filter-panel-header"
        style={{
          cursor: "pointer",
          userSelect: "none",
          marginTop: "0.5rem",
        }}
        onClick={() => setIsTrackerOpen(!isTrackerOpen)}
      >
        <div
          className="filter-panel-section"
          style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
        >
          <span style={{ fontSize: "0.7rem" }}>
            {isTrackerOpen ? "▼" : "▶"}
          </span>
          <span>{t("torrents.filterTracker", undefined, "Tracker")}</span>
        </div>
      </div>
      {isTrackerOpen && (
        <ul className="filter-panel-list">
          <li>
            <button
              className={`filter-panel-item${selectedTracker === "All" ? " active" : ""}`}
              onClick={() => onSelectTracker("All")}
            >
              <span className="filter-panel-label">
                <AllIcon size={13} /> {t("common.all", undefined, "All")}
              </span>
              <span className="filter-panel-count">{count}</span>
            </button>
          </li>
          {trackerGroups.map(([domain, groupCount]) => (
            <li key={domain}>
              <button
                className={`filter-panel-item${selectedTracker === domain ? " active" : ""}`}
                onClick={() => onSelectTracker(domain)}
              >
                <span
                  className="filter-panel-label"
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <TrackerFavicon urlOrHost={domain} size={14} />
                  <span
                    style={{
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {domain}
                  </span>
                </span>
                <span className="filter-panel-count">{groupCount}</span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {/* Category Filter Section */}
      <div
        className="filter-panel-header"
        style={{
          cursor: "pointer",
          userSelect: "none",
          marginTop: "0.5rem",
        }}
        onClick={() => setIsCategoryOpen(!isCategoryOpen)}
      >
        <div
          className="filter-panel-section"
          style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
        >
          <span style={{ fontSize: "0.7rem" }}>
            {isCategoryOpen ? "▼" : "▶"}
          </span>
          <span>📁 {t("torrents.filterCategory", undefined, "Category")}</span>
        </div>
      </div>
      {isCategoryOpen && (
        <ul className="filter-panel-list">
          <li>
            <button
              className={`filter-panel-item${selectedCategory === "All" ? " active" : ""}`}
              onClick={() => onSelectCategory?.("All")}
            >
              <span className="filter-panel-label">
                <AllIcon size={13} /> {t("common.all", undefined, "All")}
              </span>
              <span className="filter-panel-count">{count}</span>
            </button>
          </li>
          {categoryGroups.map(([cat, groupCount]) => (
            <li key={cat}>
              <button
                className={`filter-panel-item${selectedCategory === cat ? " active" : ""}`}
                onClick={() => onSelectCategory?.(cat)}
              >
                <span
                  className="filter-panel-label"
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.4rem",
                  }}
                >
                  <span style={{ fontSize: "0.85rem" }}>📁</span>
                  <span
                    style={{
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {cat}
                  </span>
                </span>
                <span className="filter-panel-count">{groupCount}</span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {/* Tag / Label Filter Section */}
      <div
        className="filter-panel-header"
        style={{
          cursor: "pointer",
          userSelect: "none",
          marginTop: "0.5rem",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
        }}
        onClick={() => setIsTagOpen(!isTagOpen)}
      >
        <div
          className="filter-panel-section"
          style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
        >
          <span style={{ fontSize: "0.7rem" }}>{isTagOpen ? "▼" : "▶"}</span>
          <span>🏷️ {t("torrents.filterTag", undefined, "Tag / Label")}</span>
        </div>
        {selectedTagIds && selectedTagIds.size > 0 && onClearTags && (
          <button
            type="button"
            className="filter-panel-clear-tags-btn"
            style={{
              background: "none",
              border: "1px solid var(--border-color, #333)",
              borderRadius: "4px",
              color: "var(--accent, #60a5fa)",
              fontSize: "0.68rem",
              padding: "1px 5px",
              cursor: "pointer",
            }}
            onClick={(e) => {
              e.stopPropagation();
              onClearTags();
            }}
            title={t("torrents.clearTags", undefined, "Clear selected tags")}
          >
            {t("common.clear", undefined, "Clear")} ({selectedTagIds.size})
          </button>
        )}
      </div>
      {isTagOpen && (
        <>
          {/* AND / OR Match Mode Toggle */}
          {onTagMatchModeChange && normalizedTagGroups.length > 0 && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.2rem 0.75rem 0.4rem",
                fontSize: "0.75rem",
              }}
            >
              <span
                style={{
                  color: "var(--text-muted, #888)",
                  fontSize: "0.7rem",
                  fontWeight: 600,
                }}
              >
                {t("torrents.tagMatchMode", undefined, "MATCH")}:
              </span>
              <div
                style={{
                  display: "inline-flex",
                  borderRadius: "4px",
                  overflow: "hidden",
                  border: "1px solid var(--border-color, #333)",
                }}
              >
                <button
                  type="button"
                  onClick={() => onTagMatchModeChange("OR")}
                  style={{
                    padding: "2px 8px",
                    fontSize: "0.7rem",
                    fontWeight: 600,
                    background:
                      tagMatchMode === "OR"
                        ? "var(--accent-bg-medium, rgba(59, 130, 246, 0.2))"
                        : "transparent",
                    color:
                      tagMatchMode === "OR"
                        ? "var(--accent, #60a5fa)"
                        : "var(--text-muted, #888)",
                    border: "none",
                    cursor: "pointer",
                  }}
                  title={t(
                    "torrents.matchAnyTagTooltip",
                    undefined,
                    "Match torrents with ANY selected tag",
                  )}
                >
                  {t("common.or", undefined, "OR")}
                </button>
                <button
                  type="button"
                  onClick={() => onTagMatchModeChange("AND")}
                  style={{
                    padding: "2px 8px",
                    fontSize: "0.7rem",
                    fontWeight: 600,
                    background:
                      tagMatchMode === "AND"
                        ? "var(--accent-bg-medium, rgba(59, 130, 246, 0.2))"
                        : "transparent",
                    color:
                      tagMatchMode === "AND"
                        ? "var(--accent, #60a5fa)"
                        : "var(--text-muted, #888)",
                    border: "none",
                    borderLeft: "1px solid var(--border-color, #333)",
                    cursor: "pointer",
                  }}
                  title={t(
                    "torrents.matchAllTagsTooltip",
                    undefined,
                    "Match torrents with ALL selected tags",
                  )}
                >
                  {t("common.and", undefined, "AND")}
                </button>
              </div>
            </div>
          )}

          <ul className="filter-panel-list">
            <li>
              <button
                className={`filter-panel-item${(!selectedTagIds || selectedTagIds.size === 0) && selectedTag === "All" ? " active" : ""}`}
                onClick={() => {
                  onClearTags?.();
                  onSelectTag?.("All");
                }}
              >
                <span className="filter-panel-label">
                  <AllIcon size={13} /> {t("common.all", undefined, "All")}
                </span>
                <span className="filter-panel-count">{count}</span>
              </button>
            </li>
            {untaggedCount !== undefined && (
              <li>
                <button
                  className={`filter-panel-item${selectedTag === "Untagged" ? " active" : ""}`}
                  onClick={() => {
                    onClearTags?.();
                    onSelectTag?.(
                      selectedTag === "Untagged" ? "All" : "Untagged",
                    );
                  }}
                >
                  <span
                    className="filter-panel-label"
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: "0.4rem",
                    }}
                  >
                    <span style={{ fontSize: "0.85rem" }}>🏷️</span>
                    <span>{t("torrents.untagged", undefined, "Untagged")}</span>
                  </span>
                  <span className="filter-panel-count">{untaggedCount}</span>
                </button>
              </li>
            )}
            {normalizedTagGroups.map((tag) => {
              const isSelected =
                Boolean(selectedTagIds?.has(tag.id)) ||
                selectedTag === tag.label;
              return (
                <li key={tag.id}>
                  <button
                    className={`filter-panel-item${isSelected ? " active" : ""}`}
                    onClick={() => {
                      if (onToggleTag) {
                        onToggleTag(tag.id);
                      } else {
                        onSelectTag?.(tag.label);
                      }
                    }}
                  >
                    <span
                      className="filter-panel-label"
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: "0.4rem",
                        minWidth: 0,
                      }}
                    >
                      {onToggleTag && (
                        <input
                          type="checkbox"
                          checked={Boolean(selectedTagIds?.has(tag.id))}
                          onChange={() => {}}
                          style={{
                            cursor: "pointer",
                            accentColor: tag.color || "var(--accent, #60a5fa)",
                            margin: 0,
                          }}
                        />
                      )}
                      {tag.color ? (
                        <span
                          style={{
                            width: "8px",
                            height: "8px",
                            borderRadius: "50%",
                            backgroundColor: tag.color,
                            display: "inline-block",
                            flexShrink: 0,
                          }}
                        />
                      ) : (
                        <span style={{ fontSize: "0.85rem" }}>🏷️</span>
                      )}
                      <span
                        style={{
                          overflow: "hidden",
                          textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}
                      >
                        {tag.label}
                      </span>
                    </span>
                    <span className="filter-panel-count">{tag.count}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </div>
  );
}
