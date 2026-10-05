// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { PauseIcon, PlayIcon } from "lucide-react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { Track } from "@/lib/types";
import { usePlayback } from "@/lib/playback/usePlayback";
import { TrackCover } from "@/components/Cover";

export function RankedList({
  tracks,
  ranked = true,
  showArtist = true,
  className,
}: {
  tracks: Track[];
  ranked?: boolean;
  showArtist?: boolean;
  className?: string;
}) {
  const { currentTrackId, playTrack, soundingNow } = usePlayback();

  return (
    <ol className={cn("grid grid-cols-2 gap-x-8 gap-y-0.5 max-md:grid-cols-1", className)}>
      {tracks.map((track, index) => {
        const current = currentTrackId === track.id;

        return (
          <li key={track.id}>
            <button
              type="button"
              onClick={() => playTrack(track, tracks)}
              className={cn(
                "group grid w-full items-center gap-3 rounded-md px-2 py-2 text-left",
                ranked
                  ? "grid-cols-[1.75rem_3.5rem_minmax(0,1fr)_auto] max-md:grid-cols-[1.75rem_3rem_minmax(0,1fr)_auto]"
                  : "grid-cols-[3.5rem_minmax(0,1fr)_auto] max-md:grid-cols-[3rem_minmax(0,1fr)_auto]",
                "transition-colors duration-150 ease-brand hover:bg-raised",
              )}
            >
              {ranked && (
                <span
                  className={cn(
                    "text-lg font-semibold text-faint tabular-nums",
                    current && "text-primary",
                  )}
                >
                  {index + 1}
                </span>
              )}

              <span className="relative size-11 justify-self-center overflow-hidden rounded-xs">
                <TrackCover track={track} />
                <span
                  aria-hidden="true"
                  className={cn(
                    "absolute inset-0 grid place-items-center bg-black/55 text-white",
                    "opacity-0 transition-opacity duration-150 ease-brand group-hover:opacity-100",
                    "group-focus-visible:opacity-100",
                    current && "opacity-100",
                  )}
                >
                  {soundingNow(track.id) ? <PauseIcon size={16} /> : <PlayIcon size={16} />}
                </span>
              </span>

              <span className="min-w-0">
                <span className={cn("block truncate font-semibold", current && "text-primary")}>
                  {track.title}
                </span>
                <span className="block truncate text-sm text-muted-foreground">
                  {showArtist ? formatArtists(track) : track.albumTitle}
                </span>
              </span>

              <span className="text-sm text-faint tabular-nums">
                {formatDuration(track.durationSeconds)}
              </span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}
