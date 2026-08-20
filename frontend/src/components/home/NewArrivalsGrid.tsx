// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import type { HomeBlock, Track } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Cover } from "../Cover";
import { PauseIcon, PlayIcon } from "../Icons";
import { Badge } from "../ui/badge";

const FRESH_DAYS = 14;

export function NewArrivalsGrid({ block, origin }: { block: HomeBlock; origin: PlaybackOrigin }) {
  const tracks = block.tracks ?? [];

  return (
    <div
      className={cn(
        "grid grid-cols-[repeat(auto-fill,minmax(10.25rem,1fr))] gap-5",
        "max-md:grid-cols-[repeat(auto-fill,minmax(8.75rem,1fr))] max-md:gap-3",
        "[&>*]:animate-rise",
      )}
    >
      {tracks.map((track, index) => (
        <Poster key={track.id} track={track} context={tracks} origin={origin} wide={index === 0} />
      ))}
    </div>
  );
}

function Poster({
  track,
  context,
  origin,
  wide,
}: {
  track: Track;
  context: Track[];
  origin: PlaybackOrigin;
  wide: boolean;
}) {
  const t = useT();
  const format = useFormat();
  const player = usePlayer();

  const isCurrent = player.currentTrack?.id === track.id;
  const isFresh = daysSince(track.createdAt) <= FRESH_DAYS;

  return (
    <button
      type="button"
      onClick={() => {
        if (isCurrent) {
          player.toggle();
          return;
        }
        player.playTrack(track, context, origin);
      }}
      className={cn(
        "group relative overflow-hidden rounded-xl bg-card text-left shadow-art",
        wide ? "col-span-2 aspect-[2/1] max-md:col-span-1 max-md:aspect-square" : "aspect-square",
      )}
    >
      <Cover
        albumId={track.albumId}
        trackId={track.id}
        hasCover={track.hasCover}
        name={track.albumTitle ?? track.title}
        variant={wide ? "full" : "thumb"}
        className="size-full rounded-none"
      />

      <span
        aria-hidden="true"
        className="absolute inset-0 bg-[linear-gradient(to_top,rgb(0_0_0/80%),rgb(0_0_0/25%)_45%,transparent_72%)]"
      />

      {isFresh && (
        <Badge className="absolute top-3 left-3 bg-white/15 text-white backdrop-blur-sm">
          {t("home.newBadge")}
        </Badge>
      )}

      <span
        aria-hidden="true"
        className={cn(
          "absolute top-3 right-3 grid size-9 place-items-center rounded-full bg-primary text-primary-foreground shadow-art",
          "transition-transform duration-150 ease-brand group-active:scale-95",
        )}
      >
        {isCurrent && player.isPlaying ? <PauseIcon size={18} /> : <PlayIcon size={18} />}
      </span>

      <span className="absolute inset-x-0 bottom-0 flex flex-col p-3">
        <span className={cn("truncate font-semibold text-white", wide && "text-lg")}>
          {track.title}
        </span>
        <span className="truncate text-sm text-white/70">{formatArtists(track)}</span>
        {wide && (
          <span className="mt-0.5 truncate text-xs text-white/60">
            {t("home.addedOn", { when: format.relativeDate(track.createdAt) })}
          </span>
        )}
      </span>
    </button>
  );
}

function daysSince(isoDate: string): number {
  const added = new Date(isoDate).getTime();
  if (Number.isNaN(added)) return Number.POSITIVE_INFINITY;

  return (Date.now() - added) / 86_400_000;
}
