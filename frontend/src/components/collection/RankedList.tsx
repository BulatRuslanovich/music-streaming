// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { Track } from "@/lib/types";
import { useNowPlaying, usePlayerActions, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { RankedRow } from "@/components/collection/RankedRow";
import { TrackCover } from "@/components/Cover";
import { PauseIcon, PlayIcon } from "@/components/Icons";

export function RankedList({
  tracks,
  origin,
  columns = 2,
  trailing,
}: {
  tracks: Track[];
  origin?: PlaybackOrigin;
  columns?: 1 | 2;
  trailing?: (track: Track, index: number) => ReactNode;
}) {
  const { currentTrackId, isPlaying } = useNowPlaying();
  const player = usePlayerActions();

  return (
    <ol
      className={cn(
        "grid gap-x-8 gap-y-0.5",
        columns === 2 ? "grid-cols-2 max-md:grid-cols-1" : "grid-cols-1",
      )}
    >
      {tracks.map((track, index) => {
        const isCurrent = currentTrackId === track.id;

        return (
          <li key={track.id} className="animate-rise">
            <RankedRow
              rank={index + 1}
              current={isCurrent}
              title={track.title}
              subtitle={formatArtists(track)}
              onClick={() => {
                if (isCurrent) {
                  player.toggle();
                  return;
                }
                player.playTrack(track, tracks, origin);
              }}
              art={
                <>
                  <TrackCover track={track} className="size-full rounded-none" />

                  <span
                    aria-hidden="true"
                    className={cn(
                      "absolute inset-0 grid place-items-center bg-black/55 text-white",
                      "opacity-0 transition-opacity duration-150 ease-brand group-hover:opacity-100",
                      "group-focus-visible:opacity-100 max-md:opacity-100",
                      isCurrent && "opacity-100",
                    )}
                  >
                    {isCurrent && isPlaying ? <PauseIcon size={16} /> : <PlayIcon size={16} />}
                  </span>
                </>
              }
              trailing={
                trailing ? (
                  trailing(track, index)
                ) : (
                  <span className="text-sm text-faint tabular-nums">
                    {formatDuration(track.durationSeconds)}
                  </span>
                )
              }
            />
          </li>
        );
      })}
    </ol>
  );
}
