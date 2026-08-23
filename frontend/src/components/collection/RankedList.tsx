// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { Track } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { TrackCover } from "@/components/Cover";
import { PauseIcon, PlayIcon } from "@/components/Icons";

/**
 * Нумерованный список треков с обложкой. Правая колонка сменная: на главной там длительность,
 * в статистике — наслушанное время с подписью, поэтому `trailing` берёт трек и отдаёт что угодно.
 */
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
  const player = usePlayer();

  return (
    <ol
      className={cn(
        "grid gap-x-8 gap-y-0.5",
        columns === 2 ? "grid-cols-2 max-md:grid-cols-1" : "grid-cols-1",
      )}
    >
      {tracks.map((track, index) => {
        const isCurrent = player.currentTrack?.id === track.id;

        return (
          <li key={track.id} className="animate-rise">
            <button
              type="button"
              onClick={() => {
                if (isCurrent) {
                  player.toggle();
                  return;
                }
                player.playTrack(track, tracks, origin);
              }}
              className={cn(
                "group grid w-full grid-cols-[1.75rem_2.75rem_minmax(0,1fr)_auto] items-center gap-3 rounded-md px-2 py-2 text-left",
                "transition-colors duration-150 ease-brand hover:bg-raised",
              )}
            >
              <span
                className={cn(
                  "text-lg font-bold text-faint tabular-nums",
                  isCurrent && "text-primary",
                )}
              >
                {index + 1}
              </span>

              <span className="relative size-11 overflow-hidden rounded-md">
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
                  {isCurrent && player.isPlaying ? <PauseIcon size={16} /> : <PlayIcon size={16} />}
                </span>
              </span>

              <span className="min-w-0">
                <span className={cn("block truncate font-semibold", isCurrent && "text-primary")}>
                  {track.title}
                </span>
                <span className="block truncate text-sm text-muted-foreground">
                  {formatArtists(track)}
                </span>
              </span>

              {trailing ? (
                trailing(track, index)
              ) : (
                <span className="text-sm text-faint tabular-nums">
                  {formatDuration(track.durationSeconds)}
                </span>
              )}
            </button>
          </li>
        );
      })}
    </ol>
  );
}
