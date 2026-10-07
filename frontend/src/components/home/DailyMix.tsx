// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Route } from "next";
import Link from "next/link";
import { cn } from "@/lib/cn";
import { displayTitleClass, formatArtists, formatDuration } from "@/lib/format";
import { usePlayback } from "@/lib/playback/usePlayback";
import type { HomeBlock } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { TrackCover } from "@/components/Cover";
import { PlayAllButton } from "@/components/PlayAllButton";
import { PlayBadge } from "@/components/PlayBadge";
import { RadioButton } from "@/components/RadioButton";
import { ShuffleButton } from "@/components/ShuffleButton";
import { capFourOnMobile } from "./layout";

export const HERO_PREVIEW_SIZE = 5;

export function DailyMix<T extends string>({
  block,
  title,
  href,
}: {
  block: HomeBlock;
  title: string;
  href?: Route<T>;
}) {
  const t = useT();
  const { currentTrackId, playTrack, soundingNow } = usePlayback();

  const tracks = block.tracks ?? [];
  const lead = tracks[0];
  const collage = tracks
    .filter((track) => track.hasCover)
    .filter(
      (track, index, covered) =>
        covered.findIndex(
          (other) => (other.albumId ?? other.id) === (track.albumId ?? track.id),
        ) === index,
    )
    .slice(0, 4);

  if (!lead) return null;

  return (
    <section
      className={cn(
        "grid grid-cols-[auto_minmax(0,1fr)_minmax(18rem,22rem)] items-end gap-8",
        "max-xl:grid-cols-[auto_minmax(0,1fr)] max-md:grid-cols-1 max-md:gap-5",
      )}
      aria-labelledby="daily-mix-heading"
    >
      <div className="size-64 shrink-0 overflow-hidden rounded-xs shadow-art max-md:aspect-square max-md:size-auto max-md:w-[min(100%,15rem)]">
        {collage.length === 4 ? (
          <div className="grid size-full grid-cols-2 grid-rows-2">
            {collage.map((track) => (
              <TrackCover key={track.id} track={track} />
            ))}
          </div>
        ) : (
          <TrackCover track={lead} variant="full" />
        )}
      </div>

      <div className="flex min-w-0 flex-col gap-3">
        <p className="text-sm text-muted-foreground">{t("home.dailyMixSubtitle")}</p>
        <h2 id="daily-mix-heading" className={cn("line-clamp-2", displayTitleClass(title))}>
          {title}
        </h2>
        <p className="truncate text-muted-foreground">
          {`${t("count.tracks", { count: block.totalCount ?? tracks.length })}, ${formatArtists(lead)}`}
        </p>
        <div className="mt-2 flex flex-wrap items-center gap-2.5">
          <PlayAllButton tracks={tracks} name={title} />
          <ShuffleButton tracks={tracks} />
          <RadioButton seed={null} label={t("radio.mine")} />
        </div>
      </div>

      <div className="min-w-0 max-xl:col-span-full">
        <div className="flex items-center justify-between gap-3 px-2 pb-1.5">
          <p className="truncate text-sm text-muted-foreground">{t("home.upNext")}</p>
          {href && (
            <Link
              href={href}
              className="text-sm text-muted-foreground transition-colors duration-150 ease-brand hover:text-foreground hover:no-underline"
            >
              {t("action.seeAll")}
            </Link>
          )}
        </div>
        <ol aria-label={title} className={capFourOnMobile}>
          {tracks.slice(0, HERO_PREVIEW_SIZE).map((track) => {
            const current = currentTrackId === track.id;
            const playing = soundingNow(track.id);

            return (
              <li key={track.id}>
                <button
                  type="button"
                  onClick={() => playTrack(track, tracks)}
                  aria-label={`${playing ? t("action.pause") : t("action.play")}: ${track.title}`}
                  className={cn(
                    "group grid w-full grid-cols-[2.5rem_minmax(0,1fr)_auto] items-center gap-3 rounded-md px-2 py-1.5 text-left",
                    "transition-colors duration-150 ease-brand hover:bg-card",
                  )}
                >
                  <span className="relative size-10 overflow-hidden rounded-xs">
                    <TrackCover track={track} />
                    <PlayBadge
                      size={8}
                      playing={playing}
                      visible={current}
                      className="absolute top-1 left-1"
                    />
                  </span>
                  <span className="min-w-0">
                    <span
                      className={cn(
                        "block truncate text-sm font-medium",
                        current && "text-primary",
                      )}
                    >
                      {track.title}
                    </span>
                    <span className="block truncate text-sm text-muted-foreground">
                      {formatArtists(track)}
                    </span>
                  </span>
                  <span className="text-xs text-faint tabular-nums">
                    {formatDuration(track.durationSeconds)}
                  </span>
                </button>
              </li>
            );
          })}
        </ol>
      </div>
    </section>
  );
}
