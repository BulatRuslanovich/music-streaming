// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import type { Track } from "@/lib/types";
import { TrackCover } from "../Cover";

export function CoverMosaic({ tracks, className }: { tracks: Track[]; className?: string }) {
  const tiles = tracks.slice(0, 4);

  if (tiles.length === 0) {
    return <div className={cn("size-full bg-raised", className)} />;
  }

  if (tiles.length < 4) {
    return <TrackCover track={tiles[0]} className={cn("size-full rounded-none", className)} />;
  }

  return (
    <div className={cn("grid size-full grid-cols-2 grid-rows-2", className)}>
      {tiles.map((track) => (
        <TrackCover key={track.id} track={track} className="size-full rounded-none" />
      ))}
    </div>
  );
}
