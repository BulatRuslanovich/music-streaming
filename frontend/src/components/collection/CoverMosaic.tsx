// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import type { Track } from "@/lib/types";
import { Cover, TrackCover } from "@/components/Cover";

function Mosaic({ tiles }: { tiles: ReactNode[] }) {
  if (tiles.length === 0) return <div className="size-full bg-raised" />;

  if (tiles.length < 4) return <div className="size-full">{tiles[0]}</div>;

  return <div className="grid size-full grid-cols-2 grid-rows-2">{tiles.slice(0, 4)}</div>;
}

export function CoverMosaic({ tracks }: { tracks: Track[] }) {
  return (
    <Mosaic
      tiles={tracks.slice(0, 4).map((track) => (
        <TrackCover key={track.id} track={track} />
      ))}
    />
  );
}

export function AlbumMosaic({ albumIds, name }: { albumIds: string[]; name: string }) {
  const tiles =
    albumIds.length === 0
      ? [<Cover key="none" hasCover={false} name={name} />]
      : albumIds.slice(0, 4).map((id) => <Cover key={id} albumId={id} name={name} />);

  return <Mosaic tiles={tiles} />;
}
