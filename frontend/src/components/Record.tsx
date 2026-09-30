// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Track } from "@/lib/types";
import { cn } from "@/lib/cn";
import { TrackCover } from "./Cover";

type RecordTrack = Pick<Track, "id" | "title" | "albumId" | "albumTitle" | "hasCover">;

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
            <TrackCover track={track} />
          </div>
        </div>
        <div className="record-sheen" />
      </div>

      <div className="record-sleeve">
        <TrackCover track={track} variant={sizes ? "full" : "thumb"} sizes={sizes} />
      </div>
    </div>
  );
}
