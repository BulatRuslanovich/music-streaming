// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { Track } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { TrackCover } from "@/components/Cover";
import { PlayBadge } from "@/components/PlayBadge";

const PREVIEW_SIZE = 4;

/** Общая плоская подложка для геро-блоков: Spotlight и топ-результата поиска. */
export const heroSurface = "overflow-hidden rounded-xl bg-card";

export function Spotlight({
  eyebrow,
  title,
  facts,
  actions,
  art,
  tracks,
  href,
  onPlayTrack,
  currentTrackId,
  isPlaying = false,
  headingId = "spotlight-heading",
}: {
  eyebrow?: string;
  title: string;
  facts?: ReactNode;
  actions?: ReactNode;
  art: ReactNode;
  tracks?: Track[];
  href?: string;
  onPlayTrack?: (track: Track) => void;
  currentTrackId?: string | null;
  isPlaying?: boolean;
  headingId?: string;
}) {
  const t = useT();

  const preview = tracks?.slice(0, PREVIEW_SIZE) ?? [];
  const hasPreview = preview.length > 0 && onPlayTrack !== undefined;

  return (
    <section
      className={cn(
        "grid shrink-0",
        heroSurface,
        hasPreview
          ? "grid-cols-[minmax(0,1.15fr)_minmax(17rem,0.85fr)] max-lg:grid-cols-1"
          : "grid-cols-1",
      )}
      aria-labelledby={headingId}
    >
      <div className="flex min-w-0 items-center gap-6 p-6 max-md:items-start max-md:gap-4 max-md:p-4">
        <div className="size-44 shrink-0 overflow-hidden rounded-xl shadow-art max-md:size-28">
          {art}
        </div>

        <div className="min-w-0">
          {eyebrow && (
            <p className="text-2xs font-bold tracking-wider text-primary uppercase">{eyebrow}</p>
          )}
          <h2 id={headingId} className="mt-2 truncate text-[clamp(1.5rem,1rem+1.4vw,2.35rem)]">
            {title}
          </h2>
          {facts && <p className="mt-1 truncate text-muted-foreground">{facts}</p>}
          {actions && <div className="mt-5 flex flex-wrap items-center gap-3">{actions}</div>}
        </div>
      </div>

      {hasPreview && (
        <div className="bg-raised p-3">
          <div className="flex items-center justify-between gap-3 px-2 py-1.5">
            <p className="truncate text-2xs font-bold tracking-wider text-faint uppercase">
              {t("home.upNext")}
            </p>
            {href && (
              <Link
                href={href}
                className="text-xs font-semibold text-faint transition-colors duration-150 ease-brand hover:text-foreground hover:no-underline"
              >
                {t("action.seeAll")}
              </Link>
            )}
          </div>
          <ol aria-label={title}>
            {preview.map((track, index) => (
              <li key={track.id}>
                <SpotlightTrack
                  track={track}
                  index={index}
                  current={currentTrackId === track.id}
                  playing={currentTrackId === track.id && isPlaying}
                  onPlay={() => onPlayTrack(track)}
                />
              </li>
            ))}
          </ol>
        </div>
      )}
    </section>
  );
}

function SpotlightTrack({
  track,
  index,
  current,
  playing,
  onPlay,
}: {
  track: Track;
  index: number;
  current: boolean;
  playing: boolean;
  onPlay: () => void;
}) {
  const t = useT();

  return (
    <button
      type="button"
      onClick={onPlay}
      aria-label={`${playing ? t("action.pause") : t("action.play")}: ${track.title}`}
      className={cn(
        "group grid w-full grid-cols-[1.25rem_2.5rem_minmax(0,1fr)_auto] items-center gap-3 rounded-lg px-2 py-2 text-left",
        "transition-colors duration-150 ease-brand",
        current ? "bg-primary-soft" : "hover:bg-accent",
      )}
    >
      <span className="text-xs text-faint tabular-nums">{index + 1}</span>
      <span className="relative size-10 overflow-hidden rounded-md">
        <TrackCover track={track} className="size-full rounded-none" />
        <PlayBadge
          size={8}
          iconSize={15}
          playing={playing}
          visible={current}
          className="absolute top-1 left-1"
        />
      </span>
      <span className="min-w-0">
        <span className={cn("block truncate font-semibold", current && "text-primary")}>
          {track.title}
        </span>
        <span className="block truncate text-sm text-muted-foreground">{formatArtists(track)}</span>
      </span>
      <span className="text-xs text-faint tabular-nums">
        {formatDuration(track.durationSeconds)}
      </span>
    </button>
  );
}
