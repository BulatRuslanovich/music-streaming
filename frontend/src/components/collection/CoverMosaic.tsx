// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import type { Track } from "@/lib/types";
import { Cover, TrackCover } from "@/components/Cover";

export function Mosaic({ tiles, className }: { tiles: ReactNode[]; className?: string }) {
  if (tiles.length === 0) {
    return <div className={cn("size-full bg-raised", className)} />;
  }

  if (tiles.length < 4) {
    return <div className={cn("size-full", className)}>{tiles[0]}</div>;
  }

  return (
    <div className={cn("grid size-full grid-cols-2 grid-rows-2", className)}>
      {tiles.slice(0, 4)}
    </div>
  );
}

export function CoverMosaic({ tracks, className }: { tracks: Track[]; className?: string }) {
  return (
    <Mosaic
      className={className}
      tiles={tracks.slice(0, 4).map((track) => (
        <TrackCover key={track.id} track={track} className="size-full rounded-none" />
      ))}
    />
  );
}

export function AlbumMosaic({
  albumIds,
  name,
  className,
}: {
  albumIds: string[];
  name: string;
  className?: string;
}) {
  const tiles =
    albumIds.length === 0
      ? [<Cover key="none" hasCover={false} name={name} className="size-full rounded-none" />]
      : albumIds
          .slice(0, 4)
          .map((id) => (
            <Cover key={id} albumId={id} name={name} className="size-full rounded-none" />
          ));

  return <Mosaic className={className} tiles={tiles} />;
}
