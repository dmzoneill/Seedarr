export interface SpeedSnapshot {
  totalUploaded: number;
  totalDownloaded: number;
  timestamp: number;
}

export interface CalculatedSpeed {
  uploadSpeed: number;
  downloadSpeed: number;
}

export interface SpeedDeltaResult {
  speed?: CalculatedSpeed;
  nextSnapshot: SpeedSnapshot;
  thresholdReached: boolean;
}

/**
 * Calculates upload and download speeds from cumulative byte counters over time.
 * Only updates the baseline snapshot when the elapsed time meets or exceeds thresholdSec,
 * preventing sub-second updates from resetting the timestamp and getting stuck at 0 B/s.
 */
export function computeSpeedDelta(
  current: { totalUploaded: number; totalDownloaded: number },
  prev: SpeedSnapshot | null,
  now: number = Date.now(),
  thresholdSec: number = 1.0,
): SpeedDeltaResult {
  if (!prev) {
    return {
      nextSnapshot: {
        totalUploaded: current.totalUploaded,
        totalDownloaded: current.totalDownloaded,
        timestamp: now,
      },
      thresholdReached: false,
    };
  }

  const timeDelta = (now - prev.timestamp) / 1000;
  if (timeDelta >= thresholdSec) {
    return {
      speed: {
        uploadSpeed: Math.max(
          0,
          (current.totalUploaded - prev.totalUploaded) / timeDelta,
        ),
        downloadSpeed: Math.max(
          0,
          (current.totalDownloaded - prev.totalDownloaded) / timeDelta,
        ),
      },
      nextSnapshot: {
        totalUploaded: current.totalUploaded,
        totalDownloaded: current.totalDownloaded,
        timestamp: now,
      },
      thresholdReached: true,
    };
  }

  // Threshold not reached: preserve previous baseline snapshot so elapsed time can accumulate
  return {
    nextSnapshot: prev,
    thresholdReached: false,
  };
}
