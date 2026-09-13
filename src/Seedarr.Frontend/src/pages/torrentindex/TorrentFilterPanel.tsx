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
  stateCounts: Record<string, number>;
  trackerGroups: [string, number][];
  categoryGroups?: [string, number][];
  tagGroups?: [string, number][];
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
  stateCounts,
  trackerGroups,
  categoryGroups = [],
  tagGroups = [],
  count,
  isCollapsed = false,
  onToggleCollapse,
}: TorrentFilterPanelProps) {
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
          <span style={{ fontSize: "0.7rem" }}>{isTrackerOpen ? "▼" : "▶"}</span>
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
          <span style={{ fontSize: "0.7rem" }}>{isCategoryOpen ? "▼" : "▶"}</span>
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
      </div>
      {isTagOpen && (
        <ul className="filter-panel-list">
          <li>
            <button
              className={`filter-panel-item${selectedTag === "All" ? " active" : ""}`}
              onClick={() => onSelectTag?.("All")}
            >
              <span className="filter-panel-label">
                <AllIcon size={13} /> {t("common.all", undefined, "All")}
              </span>
              <span className="filter-panel-count">{count}</span>
            </button>
          </li>
          {tagGroups.map(([tag, groupCount]) => (
            <li key={tag}>
              <button
                className={`filter-panel-item${selectedTag === tag ? " active" : ""}`}
                onClick={() => onSelectTag?.(tag)}
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
                  <span
                    style={{
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {tag}
                  </span>
                </span>
                <span className="filter-panel-count">{groupCount}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
