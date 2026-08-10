"use client";

import { useId } from "react";

interface SeekbarProps {
  value: number;
  max: number;
  step?: number;
  onSeek: (value: number) => void;
  ariaLabel: string;
  className?: string;
}

/**
 * Progress and volume slider.
 *
 * Built on a native `<input type="range">` so it works with a mouse, a touch drag and the
 * keyboard for free, including screen-reader announcements. The visible track is painted with a
 * gradient driven by a CSS custom property, which keeps the filled portion in sync without a
 * second element to position.
 */
export function Seekbar({ value, max, step, onSeek, ariaLabel, className = "" }: SeekbarProps) {
  const id = useId();
  const safeMax = max > 0 ? max : 0;
  const percent = safeMax > 0 ? Math.min(100, (value / safeMax) * 100) : 0;

  return (
    <input
      id={id}
      type="range"
      className={`seekbar ${className}`}
      min={0}
      max={safeMax || 1}
      step={step ?? 0.5}
      value={Math.min(value, safeMax || 1)}
      onChange={(event) => onSeek(Number(event.target.value))}
      aria-label={ariaLabel}
      style={{ ["--progress" as string]: `${percent}%` }}
      disabled={safeMax === 0}
    />
  );
}
