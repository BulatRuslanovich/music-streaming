"use client";

import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { HomeBlock } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { Cover } from "../Cover";
import { PauseIcon, PlayIcon } from "../Icons";

export function ChartBlock({ block, origin }: { block: HomeBlock; origin: PlaybackOrigin }) {
  const player = usePlayer();
  const tracks = block.tracks ?? [];

  return (
    <ol className="grid grid-cols-2 gap-x-8 gap-y-0.5 max-md:grid-cols-1">
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
                <Cover
                  albumId={track.albumId}
                  trackId={track.id}
                  hasCover={track.hasCover}
                  name={track.albumTitle ?? track.title}
                  className="size-full rounded-none"
                />

                <span
                  aria-hidden="true"
                  className={cn(
                    "absolute inset-0 grid place-items-center bg-black/55 text-white",
                    "opacity-0 transition-opacity duration-150 ease-brand group-hover:opacity-100",
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
