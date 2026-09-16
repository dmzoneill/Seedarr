import type { Torrent } from "../api/types";
import { extractTrackerDomain } from "./formatters";

export interface TorrentFilterCriteria {
  filter?: string;
  stateFilter?: string;
  trackerFilter?: string;
  categoryFilter?: string;
  tagFilter?: string;
}

/**
 * Filters a list of torrents based on search query, state, tracker, category, and tag filters.
 */
export function filterTorrents(
  torrents: Torrent[] | undefined,
  criteria: TorrentFilterCriteria,
): Torrent[] {
  const { filter, stateFilter, trackerFilter, categoryFilter, tagFilter } =
    criteria;

  return (torrents ?? []).filter((t) => {
    if (filter && !t.name.toLowerCase().includes(filter.toLowerCase())) {
      return false;
    }

    if (stateFilter && stateFilter !== "All" && t.status !== stateFilter) {
      return false;
    }

    if (trackerFilter && trackerFilter !== "All") {
      const urls =
        t.trackers && t.trackers.length > 0
          ? t.trackers
          : t.trackerUrl
            ? [t.trackerUrl]
            : [];
      const hasTracker = urls.some(
        (u) => extractTrackerDomain(u) === trackerFilter,
      );
      if (!hasTracker) return false;
    }

    if (categoryFilter && categoryFilter !== "All") {
      const cat = t.category?.trim() || "Uncategorized";
      if (cat !== categoryFilter) return false;
    }

    if (tagFilter && tagFilter !== "All") {
      const tag = t.label?.trim() || "Untagged";
      if (tag !== tagFilter) return false;
    }

    return true;
  });
}
