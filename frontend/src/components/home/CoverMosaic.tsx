// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import type { Track } from "@/lib/types";
import { Cover } from "../Cover";

export function CoverMosaic({ tracks, className }: { tracks: Track[]; className?: string }) {
  const tiles = tracks.slice(0, 4);

  if (tiles.length === 0) {
    return <div className={cn("size-full bg-raised", className)} />;
  }

  if (tiles.length < 4) {
    return (
      <Cover
        albumId={tiles[0].albumId}
        trackId={tiles[0].id}
        hasCover={tiles[0].hasCover}
        name={tiles[0].albumTitle ?? tiles[0].title}
        className={cn("size-full rounded-none", className)}
      />
    );
  }

  return (
    <div className={cn("grid size-full grid-cols-2 grid-rows-2", className)}>
      {tiles.map((track) => (
        <Cover
          key={track.id}
          albumId={track.albumId}
          trackId={track.id}
          hasCover={track.hasCover}
          name={track.albumTitle ?? track.title}
          className="size-full rounded-none"
        />
      ))}
    </div>
  );
}
