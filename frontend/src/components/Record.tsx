// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Track } from "@/lib/types";
import { cn } from "@/lib/cn";
import { TrackCover } from "./Cover";

type RecordTrack = Pick<Track, "id" | "title" | "albumId" | "albumTitle" | "hasCover">;

/**
 * Обложка трека как конверт пластинки, из которого выезжает диск. Яблоко диска — та же
 * обложка, обрезанная в круг, поэтому цвет пластинки всегда совпадает с конвертом.
 *
 * `out` — диск снаружи, `spinning` — крутится. `axis="y"` выдвигает его вверх, для узких
 * экранов, где справа от конверта места нет. Диск перемонтируется по треку, и смена трека
 * читается как новая пластинка: он заново выезжает из конверта.
 */
export function Record({
  track,
  out,
  spinning,
  axis = "x",
  sizes,
  className,
}: {
  track: RecordTrack;
  out: boolean;
  spinning: boolean;
  axis?: "x" | "y";
  sizes?: string;
  className?: string;
}) {
  return (
    <div
      className={cn("record", className)}
      data-out={out}
      data-spinning={spinning}
      data-axis={axis}
    >
      <div key={track.id} className="record-disc" aria-hidden="true">
        <div className="record-spin">
          <div className="record-label">
            <TrackCover track={track} size="100%" variant="thumb" className="rounded-none" />
          </div>
        </div>
        <div className="record-sheen" />
      </div>

      <div className="record-sleeve">
        <TrackCover
          track={track}
          size="100%"
          variant={sizes ? "full" : "thumb"}
          sizes={sizes}
          className="rounded-none"
        />
      </div>
    </div>
  );
}
