import React, { useState, useRef, useEffect, useCallback } from "react";
import {
  useSpeedSchedules,
  useActiveSpeedLimits,
  useCreateSpeedSchedule,
  useUpdateSpeedSchedule,
  useDeleteSpeedSchedule,
} from "../api/hooks";
import { formatSpeed } from "../utils/formatters";
import type { SpeedScheduleEntry } from "../api/types";

const DAY_FLAGS = [
  { label: "Mon", value: 1 },
  { label: "Tue", value: 2 },
  { label: "Wed", value: 4 },
  { label: "Thu", value: 8 },
  { label: "Fri", value: 16 },
  { label: "Sat", value: 32 },
  { label: "Sun", value: 64 },
];

const BLOCK_COLORS = [
  "var(--accent, #c8a84e)",
  "#27ae60",
  "#3498db",
  "#9b59b6",
  "#e67e22",
  "#1abc9c",
];

function daysToLabels(days: number): string {
  if (days === 127) return "Every day";
  if (days === 31) return "Weekdays";
  if (days === 96) return "Weekends";
  return (
    DAY_FLAGS.filter((d) => days & d.value)
      .map((d) => d.label)
      .join(", ") || "None"
  );
}

function timeToHour(time: string): number {
  const [h, m] = time.split(":").map(Number);
  return h + m / 60;
}

function isScheduleInSlot(
  s: SpeedScheduleEntry,
  dayValue: number,
  prevDayValue: number,
  hour: number
): boolean {
  const start = timeToHour(s.startTime);
  const end = timeToHour(s.endTime);
  if (start === end) {
    return Boolean(s.days & dayValue);
  }
  if (start < end) {
    return Boolean(s.days & dayValue) && hour >= start && hour < end;
  } else {
    const isTodayEvening = Boolean(s.days & dayValue) && hour >= start;
    const isPrevDayMorning = Boolean(s.days & prevDayValue) && hour < end;
    return isTodayEvening || isPrevDayMorning;
  }
}

const EMPTY_SCHEDULE: Omit<SpeedScheduleEntry, "id"> = {
  name: "",
  days: 127,
  startTime: "00:00",
  endTime: "23:59",
  maxUploadSpeed: -1,
  maxDownloadSpeed: -1,
  isEnabled: true,
  priority: 0,
};

function ScheduleModal({
  schedule,
  onSave,
  onCancel,
  isPending,
}: {
  schedule: Partial<SpeedScheduleEntry>;
  onSave: (s: Partial<SpeedScheduleEntry>) => void;
  onCancel: () => void;
  isPending: boolean;
}) {
  const [form, setForm] = useState({ ...EMPTY_SCHEDULE, ...schedule });

  function toggleDay(value: number) {
    setForm({ ...form, days: form.days ^ value });
  }

  return (
    <div className="modal-overlay" onClick={onCancel}>
      <div
        className="modal"
        onClick={(e) => e.stopPropagation()}
        style={{
          maxWidth: 520,
          borderRadius: "8px",
          boxShadow: "0 16px 40px rgba(0, 0, 0, 0.7)",
          border: "1px solid var(--border-light)",
        }}
      >
        <h2 style={{ margin: "0 0 1.25rem", fontSize: "1.25rem" }}>
          {schedule.id ? "Edit Speed Schedule" : "Add Speed Schedule"}
        </h2>
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <label>
            <span
              className="status-label"
              style={{
                display: "block",
                marginBottom: "0.25rem",
                fontWeight: 600,
                fontSize: "0.82rem",
              }}
            >
              Schedule Name
            </span>
            <input
              className="form-input"
              type="text"
              placeholder="e.g. Night Seeding Boost"
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              style={{ width: "100%", borderRadius: "6px" }}
            />
          </label>

          <div>
            <span
              className="status-label"
              style={{
                display: "block",
                marginBottom: "0.4rem",
                fontWeight: 600,
                fontSize: "0.82rem",
              }}
            >
              Active Days
            </span>
            <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
              {DAY_FLAGS.map((d) => (
                <button
                  key={d.value}
                  className={`btn btn-small ${form.days & d.value ? "btn-primary" : "btn-outline"}`}
                  onClick={() => toggleDay(d.value)}
                  type="button"
                  style={{ minWidth: "42px", borderRadius: "4px" }}
                >
                  {d.label}
                </button>
              ))}
            </div>
          </div>

          <div style={{ display: "flex", gap: 12 }}>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                Start Time
              </span>
              <input
                className="form-input"
                type="time"
                value={form.startTime}
                onChange={(e) =>
                  setForm({ ...form, startTime: e.target.value })
                }
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                End Time
              </span>
              <input
                className="form-input"
                type="time"
                value={form.endTime}
                onChange={(e) => setForm({ ...form, endTime: e.target.value })}
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
          </div>

          <div style={{ display: "flex", gap: 12 }}>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                Max Upload (KB/s, -1 = unlimited, 0 = pause)
              </span>
              <input
                className="form-input"
                type="number"
                min={-1}
                value={form.maxUploadSpeed < 0 ? -1 : form.maxUploadSpeed / 1024}
                onChange={(e) => {
                  const val = Number(e.target.value);
                  setForm({
                    ...form,
                    maxUploadSpeed: val < 0 ? -1 : Math.round(val * 1024),
                  });
                }}
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                Max Download (KB/s, -1 = unlimited, 0 = pause)
              </span>
              <input
                className="form-input"
                type="number"
                min={-1}
                value={form.maxDownloadSpeed < 0 ? -1 : form.maxDownloadSpeed / 1024}
                onChange={(e) => {
                  const val = Number(e.target.value);
                  setForm({
                    ...form,
                    maxDownloadSpeed: val < 0 ? -1 : Math.round(val * 1024),
                  });
                }}
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
          </div>

          <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
            <label style={{ flex: 1 }}>
              <span
                className="status-label"
                style={{
                  display: "block",
                  marginBottom: "0.25rem",
                  fontWeight: 600,
                  fontSize: "0.82rem",
                }}
              >
                Priority
              </span>
              <input
                className="form-input"
                type="number"
                value={form.priority}
                onChange={(e) =>
                  setForm({ ...form, priority: Number(e.target.value) })
                }
                style={{ width: "100%", borderRadius: "6px" }}
              />
            </label>
            <label
              style={{
                flex: 1,
                display: "flex",
                alignItems: "center",
                gap: 8,
                paddingTop: 18,
                cursor: "pointer",
              }}
            >
              <input
                type="checkbox"
                checked={form.isEnabled}
                onChange={(e) =>
                  setForm({ ...form, isEnabled: e.target.checked })
                }
              />
              <span style={{ fontWeight: 600, fontSize: "0.85rem" }}>
                Schedule Enabled
              </span>
            </label>
          </div>

          <div
            style={{
              display: "flex",
              gap: 8,
              justifyContent: "flex-end",
              marginTop: 10,
              paddingTop: 12,
              borderTop: "1px solid var(--border-light)",
            }}
          >
            <button
              className="btn btn-outline btn-small"
              onClick={onCancel}
              type="button"
            >
              Cancel
            </button>
            <button
              className="btn btn-primary btn-small"
              onClick={() => onSave(form)}
              disabled={isPending || !form.name.trim()}
              type="button"
            >
              {isPending ? "Saving..." : "Save Schedule"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

interface WeeklyCalendarProps {
  schedules: SpeedScheduleEntry[];
  onSelectRange?: (range: {
    days: number;
    startTime: string;
    endTime: string;
  }) => void;
  onToggleSchedule?: (schedule: SpeedScheduleEntry) => void;
}

function WeeklyCalendar({
  schedules,
  onSelectRange,
  onToggleSchedule,
}: WeeklyCalendarProps) {
  const hours = Array.from({ length: 24 }, (_, i) => i);

  const [isDragging, setIsDragging] = useState(false);
  const [dragStart, setDragStart] = useState<{
    dayIdx: number;
    hour: number;
  } | null>(null);
  const [dragCurrent, setDragCurrent] = useState<{
    dayIdx: number;
    hour: number;
  } | null>(null);

  const [selectedRange, setSelectedRange] = useState<{
    minDay: number;
    maxDay: number;
    minHour: number;
    maxHour: number;
    days: number;
    startTime: string;
    endTime: string;
  } | null>(null);

  const isDraggingRef = useRef(false);
  const dragStartRef = useRef<{ dayIdx: number; hour: number } | null>(null);
  const dragCurrentRef = useRef<{ dayIdx: number; hour: number } | null>(null);
  const hasMovedRef = useRef(false);
  const schedulesRef = useRef(schedules);
  schedulesRef.current = schedules;
  const onToggleScheduleRef = useRef(onToggleSchedule);
  onToggleScheduleRef.current = onToggleSchedule;

  const getScheduleForSlot = useCallback(
    (dayIdx: number, hour: number) => {
      const currentSchedules = schedulesRef.current;
      const day = DAY_FLAGS[dayIdx];
      const prevDay = DAY_FLAGS[(dayIdx + 6) % 7];
      const matching = currentSchedules.filter((s) =>
        isScheduleInSlot(s, day.value, prevDay.value, hour)
      );
      const active = matching.filter((s) => s.isEnabled);
      if (active.length > 0) {
        return [...active].sort(
          (a, b) => (a.priority ?? 999) - (b.priority ?? 999)
        )[0];
      }
      if (matching.length > 0) {
        return [...matching].sort(
          (a, b) => (a.priority ?? 999) - (b.priority ?? 999)
        )[0];
      }
      return null;
    },
    []
  );

  const finishDrag = useCallback(() => {
    if (!isDraggingRef.current) return;
    isDraggingRef.current = false;
    setIsDragging(false);

    const start = dragStartRef.current;
    const current = dragCurrentRef.current;
    const hasMoved = hasMovedRef.current;

    if (!start || !current) return;

    const minD = Math.min(start.dayIdx, current.dayIdx);
    const maxD = Math.max(start.dayIdx, current.dayIdx);
    const minH = Math.min(start.hour, current.hour);
    const maxH = Math.max(start.hour, current.hour);

    if (!hasMoved) {
      // Single click toggle on cell with an existing schedule
      const clickedSlotSchedule = getScheduleForSlot(start.dayIdx, start.hour);
      if (clickedSlotSchedule) {
        onToggleScheduleRef.current?.(clickedSlotSchedule);
        setSelectedRange(null);
        return;
      }
    }

    // Either a drag across cells or a single click on an empty cell
    let dayMask = 0;
    for (let i = minD; i <= maxD; i++) {
      dayMask |= DAY_FLAGS[i].value;
    }
    const startTime = `${String(minH).padStart(2, "0")}:00`;
    const endTime =
      maxH === 23 ? "23:59" : `${String(maxH + 1).padStart(2, "0")}:00`;

    setSelectedRange({
      minDay: minD,
      maxDay: maxD,
      minHour: minH,
      maxHour: maxH,
      days: dayMask,
      startTime,
      endTime,
    });
  }, [getScheduleForSlot]);

  // Window mouseup and touchend listeners when dragging begins
  useEffect(() => {
    if (!isDragging) return;

    const handleGlobalMouseUp = () => {
      finishDrag();
    };
    const handleGlobalTouchEnd = () => {
      finishDrag();
    };

    window.addEventListener("mouseup", handleGlobalMouseUp);
    window.addEventListener("touchend", handleGlobalTouchEnd);
    window.addEventListener("touchcancel", handleGlobalTouchEnd);
    return () => {
      window.removeEventListener("mouseup", handleGlobalMouseUp);
      window.removeEventListener("touchend", handleGlobalTouchEnd);
      window.removeEventListener("touchcancel", handleGlobalTouchEnd);
    };
  }, [isDragging, finishDrag]);

  const handleMouseDown = (
    e: React.MouseEvent,
    dayIdx: number,
    hour: number
  ) => {
    if (e.button !== 0) return;
    e.preventDefault();
    isDraggingRef.current = true;
    dragStartRef.current = { dayIdx, hour };
    dragCurrentRef.current = { dayIdx, hour };
    hasMovedRef.current = false;
    setIsDragging(true);
    setDragStart({ dayIdx, hour });
    setDragCurrent({ dayIdx, hour });
  };

  const handleMouseEnter = (dayIdx: number, hour: number) => {
    if (!isDraggingRef.current) return;
    if (
      dragStartRef.current &&
      (dragStartRef.current.dayIdx !== dayIdx ||
        dragStartRef.current.hour !== hour)
    ) {
      hasMovedRef.current = true;
    }
    dragCurrentRef.current = { dayIdx, hour };
    setDragCurrent({ dayIdx, hour });
  };

  const handleTouchStart = (
    e: React.TouchEvent,
    dayIdx: number,
    hour: number
  ) => {
    isDraggingRef.current = true;
    dragStartRef.current = { dayIdx, hour };
    dragCurrentRef.current = { dayIdx, hour };
    hasMovedRef.current = false;
    setIsDragging(true);
    setDragStart({ dayIdx, hour });
    setDragCurrent({ dayIdx, hour });
  };

  const handleTouchMove = (e: React.TouchEvent) => {
    if (!isDraggingRef.current) return;
    const touch = e.touches[0];
    if (!touch) return;
    const target = document.elementFromPoint(touch.clientX, touch.clientY);
    if (!target) return;
    const cell = target.closest<HTMLElement>("[data-calendar-cell]");
    if (
      cell &&
      cell.dataset.dayIdx !== undefined &&
      cell.dataset.hour !== undefined
    ) {
      const d = parseInt(cell.dataset.dayIdx, 10);
      const h = parseInt(cell.dataset.hour, 10);
      if (!isNaN(d) && !isNaN(h)) {
        if (
          dragStartRef.current &&
          (dragStartRef.current.dayIdx !== d || dragStartRef.current.hour !== h)
        ) {
          hasMovedRef.current = true;
        }
        dragCurrentRef.current = { dayIdx: d, hour: h };
        setDragCurrent({ dayIdx: d, hour: h });
      }
    }
  };

  const handleTouchEnd = () => {
    finishDrag();
  };

  const selectedOverlappingSchedules = selectedRange
    ? schedules.filter((s) => {
        for (let d = selectedRange.minDay; d <= selectedRange.maxDay; d++) {
          const day = DAY_FLAGS[d];
          const prevDay = DAY_FLAGS[(d + 6) % 7];
          for (let h = selectedRange.minHour; h <= selectedRange.maxHour; h++) {
            if (isScheduleInSlot(s, day.value, prevDay.value, h)) {
              return true;
            }
          }
        }
        return false;
      })
    : [];

  return (
    <div
      className="card"
      style={{
        overflowX: "auto",
        marginBottom: "1.25rem",
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
        border: "1px solid var(--border-light)",
        padding: "1.25rem",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
          flexWrap: "wrap",
          gap: "0.5rem",
        }}
      >
        <h3 style={{ margin: 0, fontSize: "1.05rem" }}>Weekly Schedule View</h3>
        <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
          Drag across grid to paint window · Click block to toggle · Touch supported
        </span>
      </div>

      <div
        style={{
          display: "grid",
          gridTemplateColumns: "55px repeat(7, 1fr)",
          gap: 0,
          minWidth: 640,
          border: "1px solid var(--border-light)",
          borderRadius: "6px",
          overflow: "hidden",
          touchAction: "none",
          userSelect: "none",
        }}
        onTouchMove={handleTouchMove}
        onTouchEnd={handleTouchEnd}
      >
        <div style={{ backgroundColor: "var(--bg-secondary)" }} />
        {DAY_FLAGS.map((d) => (
          <div
            key={d.value}
            style={{
              textAlign: "center",
              fontWeight: 600,
              fontSize: "0.82rem",
              padding: "6px 0",
              backgroundColor: "var(--bg-secondary)",
              borderBottom: "1px solid var(--border-light)",
              borderLeft: "1px solid var(--border-light)",
              color: "var(--accent, #c8a84e)",
              userSelect: "none",
            }}
          >
            {d.label}
          </div>
        ))}
        {hours.map((hour) => (
          <React.Fragment key={hour}>
            <div
              style={{
                fontSize: "0.72rem",
                color: "var(--text-muted)",
                textAlign: "right",
                paddingRight: 8,
                paddingTop: 3,
                borderTop: "1px solid var(--border-light)",
                backgroundColor: "var(--bg-secondary)",
                fontFamily: "monospace",
                userSelect: "none",
              }}
            >
              {String(hour).padStart(2, "0")}:00
            </div>
            {DAY_FLAGS.map((day, dayIdx) => {
              const prevDay = DAY_FLAGS[(dayIdx + 6) % 7];
              const matching = schedules.filter((s) =>
                isScheduleInSlot(s, day.value, prevDay.value, hour)
              );
              const active = matching.filter((s) => s.isEnabled);
              const top = [...active].sort(
                (a, b) => (a.priority ?? 999) - (b.priority ?? 999)
              )[0];
              const inactiveTop = !top
                ? [...matching].sort(
                    (a, b) => (a.priority ?? 999) - (b.priority ?? 999)
                  )[0]
                : undefined;

              const isBeingDragged =
                isDragging &&
                dragStart !== null &&
                dragCurrent !== null &&
                dayIdx >= Math.min(dragStart.dayIdx, dragCurrent.dayIdx) &&
                dayIdx <= Math.max(dragStart.dayIdx, dragCurrent.dayIdx) &&
                hour >= Math.min(dragStart.hour, dragCurrent.hour) &&
                hour <= Math.max(dragStart.hour, dragCurrent.hour);

              const isRangeSelected =
                selectedRange !== null &&
                dayIdx >= selectedRange.minDay &&
                dayIdx <= selectedRange.maxDay &&
                hour >= selectedRange.minHour &&
                hour <= selectedRange.maxHour;

              const isHighlighted = isBeingDragged || isRangeSelected;

              return (
                <div
                  key={`${hour}-${day.value}`}
                  data-calendar-cell="true"
                  data-day-idx={dayIdx}
                  data-hour={hour}
                  onMouseDown={(e) => handleMouseDown(e, dayIdx, hour)}
                  onMouseEnter={() => handleMouseEnter(dayIdx, hour)}
                  onTouchStart={(e) => handleTouchStart(e, dayIdx, hour)}
                  onTouchMove={handleTouchMove}
                  onTouchEnd={handleTouchEnd}
                  onDoubleClick={() => {
                    const sTime = `${String(hour).padStart(2, "0")}:00`;
                    const eTime =
                      hour === 23
                        ? "23:59"
                        : `${String(hour + 1).padStart(2, "0")}:00`;
                    onSelectRange?.({
                      days: day.value,
                      startTime: sTime,
                      endTime: eTime,
                    });
                  }}
                  style={{
                    height: 22,
                    borderTop: "1px solid var(--border-light)",
                    borderLeft: "1px solid var(--border-light)",
                    backgroundColor: isHighlighted
                      ? "rgba(200, 168, 78, 0.45)"
                      : top
                      ? BLOCK_COLORS[
                          schedules.indexOf(top) % BLOCK_COLORS.length
                        ]
                      : inactiveTop
                      ? "rgba(255, 255, 255, 0.05)"
                      : "transparent",
                    opacity: isHighlighted
                      ? 1
                      : top
                      ? 0.85
                      : inactiveTop
                      ? 0.35
                      : 1,
                    outline: isHighlighted
                      ? "2px solid var(--accent, #c8a84e)"
                      : inactiveTop
                      ? "1px dashed rgba(255, 255, 255, 0.2)"
                      : "none",
                    outlineOffset: isHighlighted ? "-2px" : "-1px",
                    cursor: "pointer",
                    touchAction: "none",
                    userSelect: "none",
                    WebkitUserSelect: "none",
                    transition: isDragging ? "none" : "all 0.15s ease",
                    position: "relative",
                    zIndex: isHighlighted ? 2 : 1,
                  }}
                  title={
                    isHighlighted
                      ? `Selected: ${day.label} ${String(hour).padStart(2, "0")}:00`
                      : top
                      ? `${top.name} (Active - click to toggle): ${top.maxUploadSpeed >= 0 ? (top.maxUploadSpeed === 0 ? "0 B/s (Paused)" : formatSpeed(top.maxUploadSpeed)) : "Unlimited"} up / ${top.maxDownloadSpeed >= 0 ? (top.maxDownloadSpeed === 0 ? "0 B/s (Paused)" : formatSpeed(top.maxDownloadSpeed)) : "Unlimited"} down`
                      : inactiveTop
                      ? `${inactiveTop.name} (Disabled - click to enable)`
                      : `Unthrottled (${day.label} ${String(hour).padStart(2, "0")}:00 - click to toggle, drag to paint)`
                  }
                />
              );
            })}
          </React.Fragment>
        ))}
      </div>

      {selectedRange && (
        <div
          style={{
            marginTop: "1rem",
            padding: "0.75rem 1rem",
            backgroundColor: "var(--bg-secondary)",
            borderRadius: "6px",
            border: "1px solid var(--accent, #c8a84e)",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "0.75rem",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
            <span style={{ fontSize: "1.1rem" }}>✨</span>
            <div>
              <div style={{ fontWeight: 600, fontSize: "0.88rem" }}>
                Selected Range: {daysToLabels(selectedRange.days)} (
                {selectedRange.startTime} - {selectedRange.endTime})
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                {selectedRange.maxHour - selectedRange.minHour + 1} hr
                {selectedRange.maxHour - selectedRange.minHour + 1 > 1 ? "s" : ""}{" "}
                per day across{" "}
                {selectedRange.maxDay - selectedRange.minDay + 1} day
                {selectedRange.maxDay - selectedRange.minDay + 1 > 1 ? "s" : ""}
              </div>
            </div>
          </div>

          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <button
              type="button"
              className="btn btn-primary btn-small"
              onClick={() => {
                onSelectRange?.({
                  days: selectedRange.days,
                  startTime: selectedRange.startTime,
                  endTime: selectedRange.endTime,
                });
                setSelectedRange(null);
              }}
            >
              + Add Schedule for Range
            </button>
            {selectedOverlappingSchedules.length > 0 && (
              <button
                type="button"
                className="btn btn-outline btn-small"
                onClick={() => {
                  selectedOverlappingSchedules.forEach((s) =>
                    onToggleSchedule?.(s)
                  );
                  setSelectedRange(null);
                }}
              >
                Toggle {selectedOverlappingSchedules.length} Schedule
                {selectedOverlappingSchedules.length > 1 ? "s" : ""}
              </button>
            )}
            <button
              type="button"
              className="btn btn-outline btn-small"
              onClick={() => setSelectedRange(null)}
            >
              Clear
            </button>
          </div>
        </div>
      )}

      {schedules.length > 0 && (
        <div
          style={{ display: "flex", gap: 16, marginTop: 14, flexWrap: "wrap" }}
        >
          {schedules.map((s, i) => (
            <div
              key={s.id}
              onClick={() => onToggleSchedule?.(s)}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 6,
                cursor: onToggleSchedule ? "pointer" : "default",
                userSelect: "none",
                padding: "2px 6px",
                borderRadius: "4px",
                backgroundColor: "rgba(255, 255, 255, 0.03)",
              }}
              title="Click to toggle schedule enabled/disabled"
            >
              <div
                style={{
                  width: 12,
                  height: 12,
                  borderRadius: 3,
                  backgroundColor: BLOCK_COLORS[i % BLOCK_COLORS.length],
                  opacity: s.isEnabled ? 0.9 : 0.3,
                }}
              />
              <span
                style={{
                  fontSize: "0.82rem",
                  opacity: s.isEnabled ? 1 : 0.5,
                  textDecoration: s.isEnabled ? "none" : "line-through",
                }}
              >
                {s.name} ({s.startTime} - {s.endTime})
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function SpeedSchedule() {
  const { data: schedules, isLoading, isError } = useSpeedSchedules();
  const { data: activeLimits } = useActiveSpeedLimits();
  const createSchedule = useCreateSpeedSchedule();
  const updateSchedule = useUpdateSpeedSchedule();
  const deleteSchedule = useDeleteSpeedSchedule();

  const [modal, setModal] = useState<Partial<SpeedScheduleEntry> | null>(null);

  function handleSave(form: Partial<SpeedScheduleEntry>) {
    if (form.id) {
      updateSchedule.mutate(form as SpeedScheduleEntry, {
        onSuccess: () => setModal(null),
      });
    } else {
      createSchedule.mutate(form, { onSuccess: () => setModal(null) });
    }
  }

  const scheduleCount = schedules?.length ?? 0;

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>📅</span> Speed Schedule ({scheduleCount})
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Manage time-based upload and download speed throttles and seeding priorities
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          <button
            className="btn btn-primary"
            onClick={() => setModal({ ...EMPTY_SCHEDULE })}
          >
            + Add Schedule
          </button>
        </div>
      </div>

      {/* Active Rate Limits Stat Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
          gap: "1rem",
          marginBottom: "1.25rem",
        }}
      >
        <div
          className="card"
          style={{
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-between",
            padding: "1rem 1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            border: "1px solid var(--border-light)",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
            }}
          >
            Active Schedule
          </div>
          <div
            style={{
              fontSize: "1.3rem",
              fontWeight: 700,
              color: "var(--text-primary)",
              margin: "0.35rem 0",
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            {activeLimits?.isScheduleActive ? (
              <span
                className="badge badge-primary"
                style={{ fontSize: "0.85rem" }}
              >
                ⚡ {activeLimits.activeScheduleName}
              </span>
            ) : (
              <span style={{ color: "var(--text-muted)", fontSize: "1.05rem" }}>
                None (Global Rate)
              </span>
            )}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {activeLimits?.isScheduleActive
              ? "Time-based scheduled rate is enforced"
              : "Standard global rate configuration"}
          </div>
        </div>

        <div
          className="card"
          style={{
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-between",
            padding: "1rem 1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            border: "1px solid var(--border-light)",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
            }}
          >
            Active Upload Limit
          </div>
          <div
            style={{
              fontSize: "1.3rem",
              fontWeight: 700,
              color: "var(--accent, #c8a84e)",
              margin: "0.35rem 0",
            }}
          >
            {activeLimits && activeLimits.maxUploadSpeed >= 0
              ? (activeLimits.maxUploadSpeed === 0 ? "0 B/s (Paused)" : formatSpeed(activeLimits.maxUploadSpeed))
              : "Unlimited"}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {activeLimits && activeLimits.maxUploadSpeed >= 0
              ? "Enforced rate throttle across active torrents"
              : "No upload bandwidth restriction"}
          </div>
        </div>

        <div
          className="card"
          style={{
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-between",
            padding: "1rem 1.25rem",
            borderRadius: "8px",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            border: "1px solid var(--border-light)",
          }}
        >
          <div
            style={{
              fontSize: "0.75rem",
              fontWeight: 600,
              color: "var(--text-muted)",
              textTransform: "uppercase",
              letterSpacing: "0.5px",
            }}
          >
            Active Download Limit
          </div>
          <div
            style={{
              fontSize: "1.3rem",
              fontWeight: 700,
              color: "var(--accent, #c8a84e)",
              margin: "0.35rem 0",
            }}
          >
            {activeLimits && activeLimits.maxDownloadSpeed >= 0
              ? (activeLimits.maxDownloadSpeed === 0 ? "0 B/s (Paused)" : formatSpeed(activeLimits.maxDownloadSpeed))
              : "Unlimited"}
          </div>
          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
            {activeLimits && activeLimits.maxDownloadSpeed >= 0
              ? "Enforced rate throttle across downloads"
              : "No download bandwidth restriction"}
          </div>
        </div>
      </div>

      {/* Weekly View */}
      {!isLoading && !isError && (
        <WeeklyCalendar
          schedules={schedules ?? []}
          onSelectRange={(range) => {
            setModal({
              ...EMPTY_SCHEDULE,
              days: range.days,
              startTime: range.startTime,
              endTime: range.endTime,
            });
          }}
          onToggleSchedule={(s) => {
            updateSchedule.mutate({
              ...s,
              isEnabled: !s.isEnabled,
            });
          }}
        />
      )}

      {/* Schedules Table */}
      <div
        className="card"
        style={{
          borderRadius: "8px",
          boxShadow:
            "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
          border: "1px solid var(--border-light)",
          padding: "1.25rem",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: "1rem",
          }}
        >
          <h3 style={{ margin: 0, fontSize: "1.05rem" }}>
            Configured Schedules ({scheduleCount})
          </h3>
        </div>

        {isLoading ? (
          <p className="loading">Loading schedules...</p>
        ) : isError ? (
          <p className="error">Failed to load schedule data.</p>
        ) : (
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Status</th>
                  <th className="torrent-table-th">Name</th>
                  <th className="torrent-table-th">Days</th>
                  <th className="torrent-table-th">Time Window</th>
                  <th className="torrent-table-th">Upload Limit</th>
                  <th className="torrent-table-th">Download Limit</th>
                  <th className="torrent-table-th">Priority</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {(schedules ?? []).length === 0 ? (
                  <tr>
                    <td
                      colSpan={8}
                      style={{ textAlign: "center", padding: "2.5rem 1rem" }}
                    >
                      <div style={{ fontSize: "2rem", marginBottom: "0.5rem" }}>
                        ⏱️
                      </div>
                      <div
                        style={{
                          fontWeight: 600,
                          fontSize: "1rem",
                          color: "var(--text-secondary)",
                          marginBottom: "0.25rem",
                        }}
                      >
                        No speed schedules configured
                      </div>
                      <div
                        style={{
                          fontSize: "0.85rem",
                          color: "var(--text-muted)",
                          maxWidth: "440px",
                          margin: "0 auto 1.25rem",
                        }}
                      >
                        Create scheduled speed rules to throttle bandwidth or
                        prioritize seeding during specific hours of the day.
                      </div>
                      <button
                        className="btn btn-primary btn-small"
                        onClick={() => setModal({ ...EMPTY_SCHEDULE })}
                      >
                        + Add First Schedule
                      </button>
                    </td>
                  </tr>
                ) : (
                  (schedules ?? []).map((s) => (
                    <tr key={s.id} className="torrent-table-row">
                      <td>
                        <span
                          className={`badge ${s.isEnabled ? "badge-primary" : "badge-secondary"}`}
                          style={{ fontSize: "0.75rem" }}
                        >
                          {s.isEnabled ? "Enabled" : "Disabled"}
                        </span>
                      </td>
                      <td style={{ fontWeight: 600 }}>{s.name}</td>
                      <td>
                        <span
                          className="badge"
                          style={{
                            backgroundColor: "var(--bg-secondary)",
                            border: "1px solid var(--border-light)",
                            fontSize: "0.75rem",
                          }}
                        >
                          {daysToLabels(s.days)}
                        </span>
                      </td>
                      <td
                        style={{ fontFamily: "monospace", fontSize: "0.85rem" }}
                      >
                        {s.startTime} - {s.endTime}
                      </td>
                      <td
                        style={{
                          color: "var(--accent, #c8a84e)",
                          fontWeight: 600,
                        }}
                      >
                        {s.maxUploadSpeed >= 0
                          ? (s.maxUploadSpeed === 0 ? "0 B/s (Paused)" : formatSpeed(s.maxUploadSpeed))
                          : "Unlimited"}
                      </td>
                      <td
                        style={{
                          color: "var(--accent, #c8a84e)",
                          fontWeight: 600,
                        }}
                      >
                        {s.maxDownloadSpeed >= 0
                          ? (s.maxDownloadSpeed === 0 ? "0 B/s (Paused)" : formatSpeed(s.maxDownloadSpeed))
                          : "Unlimited"}
                      </td>
                      <td>
                        <span
                          className="badge"
                          style={{
                            backgroundColor: "var(--bg-secondary)",
                            fontSize: "0.75rem",
                          }}
                        >
                          P{s.priority}
                        </span>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <div style={{ display: "inline-flex", gap: 6 }}>
                          <button
                            className="btn btn-small btn-outline"
                            onClick={() => setModal({ ...s })}
                          >
                            Edit
                          </button>
                          <button
                            className="btn btn-small btn-danger"
                            onClick={() => deleteSchedule.mutate(s.id)}
                          >
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {modal && (
        <ScheduleModal
          schedule={modal}
          onSave={handleSave}
          onCancel={() => setModal(null)}
          isPending={createSchedule.isPending || updateSchedule.isPending}
        />
      )}
    </div>
  );
}

export default SpeedSchedule;
