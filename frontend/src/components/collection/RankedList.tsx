// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { PauseIcon, PlayIcon } from "lucide-react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { Track } from "@/lib/types";
import { usePlayback } from "@/lib/playback/usePlayback";
import { TrackCover } from "@/components/Cover";

/** Пронумерованный чарт треков в две колонки: ранг, обложка, название, длительность. */
export function RankedList({ tracks, className }: { tracks: Track[]; className?: string }) {
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
                // Колонка обложки фиксированная, а не `auto`: каждая строка — своя сетка, и от
                // плавающей ширины названия строки начинались бы с разных позиций.
                "group grid w-full grid-cols-[1.75rem_3.5rem_minmax(0,1fr)_auto] items-center gap-3",
                "rounded-md px-2 py-2 text-left max-md:grid-cols-[1.75rem_3rem_minmax(0,1fr)_auto]",
                "transition-colors duration-150 ease-brand hover:bg-raised",
              )}
            >
              <span
                className={cn(
                  "text-lg font-semibold text-faint tabular-nums",
                  current && "text-primary",
                )}
              >
                {index + 1}
              </span>

              <span className="relative size-11 justify-self-center overflow-hidden rounded-xs">
                <TrackCover track={track} />
                <span
                  aria-hidden="true"
                  className={cn(
                    "absolute inset-0 grid place-items-center bg-black/55 text-white",
                    "opacity-0 transition-opacity duration-150 ease-brand group-hover:opacity-100",
                    "group-focus-visible:opacity-100",
                    // Затемнение с иконкой висело на каждой строке чарта: двенадцать притушенных
                    // обложек подряд вместо списка. Строка и так нажимается целиком, так что на
                    // телефоне подсвечивается только текущая.
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
                  {formatArtists(track)}
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
