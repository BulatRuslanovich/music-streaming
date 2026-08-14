"use client";

import { SyntheticEvent, useId, useState } from "react";

interface SeekbarProps {
  value: number;
  max: number;
  step?: number;
  onSeek: (value: number) => void;
  ariaLabel: string;
  className?: string;
  /**
   * When true, dragging only updates the thumb visually — the seek (and the audio.currentTime
   * jump it causes) only commits on release, instead of firing on every drag tick. Avoids the
   * crackling that comes from repeatedly re-seeking mid-drag. Use for playback position; leave
   * off for volume, where live updates are expected and don't touch currentTime.
   */
  commitOnRelease?: boolean;
}

export function Seekbar({
  value,
  max,
  step,
  onSeek,
  ariaLabel,
  className = "",
  commitOnRelease = false,
}: SeekbarProps) {
  const id = useId();
  const safeMax = max > 0 ? max : 0;
  const [dragValue, setDragValue] = useState<number | null>(null);

  const displayValue = dragValue ?? Math.min(value, safeMax || 1);
  const percent = safeMax > 0 ? Math.min(100, (displayValue / safeMax) * 100) : 0;

  const commit = (event: SyntheticEvent<HTMLInputElement>) => {
    if (dragValue === null) return;
    setDragValue(null);
    onSeek(Number(event.currentTarget.value));
  };

  return (
    <input
      id={id}
      type="range"
      className={`seekbar ${className}`}
      min={0}
      max={safeMax || 1}
      step={step ?? 0.5}
      value={displayValue}
      onChange={(event) => {
        const next = Number(event.target.value);
        if (commitOnRelease) setDragValue(next);
        else onSeek(next);
      }}
      onPointerUp={commitOnRelease ? commit : undefined}
      onKeyUp={commitOnRelease ? commit : undefined}
      aria-label={ariaLabel}
      style={{ ["--progress" as string]: `${percent}%` }}
      disabled={safeMax === 0}
    />
  );
}
