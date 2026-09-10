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
  stateCounts: Record<string, number>;
  trackerGroups: [string, number][];
  count: number;
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
}

export function TorrentFilterPanel({
  selectedState,
  onSelectState,
  selectedTracker,
  onSelectTracker,
  stateCounts,
  trackerGroups,
  count,
  isCollapsed = false,
  onToggleCollapse,
}: TorrentFilterPanelProps) {
  const { t } = useTranslation();

  return (
    <div className={`filter-panel ${isCollapsed ? "collapsed" : ""}`}>
      <div className="filter-panel-header">
        <div className="filter-panel-section">{t("torrents.filterState", undefined, "State")}</div>
        {onToggleCollapse && (
          <button
            type="button"
            className="filter-panel-collapse-btn"
            onClick={onToggleCollapse}
            title="Collapse filter sidebar"
            aria-label="Collapse filter sidebar"
          >
            <ChevronsLeftIcon size={13} />
          </button>
        )}
      </div>
      <ul className="filter-panel-list">
        {STATE_FILTERS.map((state) => (
          <li key={state}>
            <button
              className={`filter-panel-item${selectedState === state ? " active" : ""}`}
              onClick={() => onSelectState(state)}
            >
              <span className="filter-panel-label">
                {STATE_FILTER_ICONS[state]} {state === "All" ? t("common.all", undefined, "All") : t(`torrents.${state.toLowerCase()}`, undefined, state)}
              </span>
              <span className="filter-panel-count">
                {stateCounts[state] ?? 0}
              </span>
            </button>
          </li>
        ))}
      </ul>
      <div className="filter-panel-section">{t("torrents.filterTracker", undefined, "Tracker")}</div>
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
    </div>
  );
}
