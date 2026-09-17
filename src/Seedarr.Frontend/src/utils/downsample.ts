/**
 * Largest-Triangle-Three-Buckets (LTTB) downsampling algorithm.
 *
 * Downsamples time series data down to `targetPoints` while preserving visual peaks,
 * troughs, and overall curve geometry without sacrificing peak bandwidth spikes.
 *
 * @param data Source dataset to downsample
 * @param targetPoints Desired number of points in the downsampled output
 * @param getX Accessor for the X dimension (e.g. index, timestamp)
 * @param getY Accessor for the Y dimension (e.g. speed, value)
 */
export function downsampleLTTB<T>(
  data: T[],
  targetPoints: number,
  getX: (d: T) => number,
  getY: (d: T) => number,
): T[] {
  if (!data || data.length === 0) {
    return [];
  }

  if (targetPoints <= 0) {
    return [];
  }

  if (targetPoints >= data.length) {
    return data;
  }

  if (targetPoints === 1) {
    return [data[0]];
  }

  if (targetPoints === 2) {
    return [data[0], data[data.length - 1]];
  }

  const sampled: T[] = [data[0]];
  const bucketSize = (data.length - 2) / (targetPoints - 2);

  let aIndex = 0; // Point A initially points to the first point

  for (let i = 0; i < targetPoints - 2; i++) {
    // Point C: calculate average of the next bucket (i + 1)
    const nextBucketStart = Math.floor((i + 1) * bucketSize) + 1;
    const nextBucketEnd = Math.min(
      Math.floor((i + 2) * bucketSize) + 1,
      data.length,
    );

    let avgX = 0;
    let avgY = 0;
    const nextCount = nextBucketEnd - nextBucketStart;

    if (nextCount > 0) {
      for (let j = nextBucketStart; j < nextBucketEnd; j++) {
        avgX += getX(data[j]);
        avgY += getY(data[j]);
      }
      avgX /= nextCount;
      avgY /= nextCount;
    } else {
      const last = data[data.length - 1];
      avgX = getX(last);
      avgY = getY(last);
    }

    // Current bucket (bucket i)
    const currBucketStart = Math.floor(i * bucketSize) + 1;
    const currBucketEnd = Math.min(
      Math.floor((i + 1) * bucketSize) + 1,
      data.length,
    );

    const aPoint = data[aIndex];
    const aX = getX(aPoint);
    const aY = getY(aPoint);

    let maxArea = -1;
    let maxAreaIndex = currBucketStart;

    for (let j = currBucketStart; j < currBucketEnd; j++) {
      const bX = getX(data[j]);
      const bY = getY(data[j]);

      // Triangle area = 0.5 * |(Ax - Cx)(By - Ay) - (Ax - Bx)(Cy - Ay)|
      const area =
        Math.abs((aX - avgX) * (bY - aY) - (aX - bX) * (avgY - aY)) * 0.5;

      if (area > maxArea) {
        maxArea = area;
        maxAreaIndex = j;
      }
    }

    sampled.push(data[maxAreaIndex]);
    aIndex = maxAreaIndex; // Next Point A is the chosen Point B
  }

  // Always retain the final point
  sampled.push(data[data.length - 1]);

  return sampled;
}

export interface SpeedDataPoint {
  uploadSpeed: number;
  downloadSpeed: number;
  timestamp?: number;
}

/**
 * Fixed-size circular ring buffer backed by typed Float64Array and Float32Arrays.
 *
 * Avoids object allocation and GC churn for high-frequency telemetry data streams.
 */
export class SpeedRingBuffer {
  private capacity: number;
  private timestamps: Float64Array;
  private uploadSpeeds: Float32Array;
  private downloadSpeeds: Float32Array;
  private head: number = 0;
  private count: number = 0;

  constructor(capacity: number = 1800) {
    this.capacity = capacity;
    this.timestamps = new Float64Array(capacity);
    this.uploadSpeeds = new Float32Array(capacity);
    this.downloadSpeeds = new Float32Array(capacity);
  }

  public push(
    timestamp: number,
    uploadSpeed: number,
    downloadSpeed: number,
  ): void {
    if (this.count < this.capacity) {
      const idx = (this.head + this.count) % this.capacity;
      this.timestamps[idx] = timestamp;
      this.uploadSpeeds[idx] = uploadSpeed;
      this.downloadSpeeds[idx] = downloadSpeed;
      this.count++;
    } else {
      this.timestamps[this.head] = timestamp;
      this.uploadSpeeds[this.head] = uploadSpeed;
      this.downloadSpeeds[this.head] = downloadSpeed;
      this.head = (this.head + 1) % this.capacity;
    }
  }

  public getPoints(maxCount?: number): SpeedDataPoint[] {
    const n =
      maxCount !== undefined ? Math.min(this.count, maxCount) : this.count;
    if (n === 0) return [];

    const startIndex = (this.head + this.count - n) % this.capacity;
    const result: SpeedDataPoint[] = new Array(n);

    for (let i = 0; i < n; i++) {
      const idx = (startIndex + i) % this.capacity;
      result[i] = {
        timestamp: this.timestamps[idx],
        uploadSpeed: this.uploadSpeeds[idx],
        downloadSpeed: this.downloadSpeeds[idx],
      };
    }

    return result;
  }

  public get size(): number {
    return this.count;
  }

  public get maxCapacity(): number {
    return this.capacity;
  }

  public clear(): void {
    this.head = 0;
    this.count = 0;
  }
}
