// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Route } from "next";
import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import type { Track } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { capFourOnMobile } from "@/components/collection/layout";
import { TrackCover } from "@/components/Cover";
import { PlayBadge } from "@/components/PlayBadge";

/**
 * Микс дня на главной — единственный якорь страницы без своей шапки. Отличается от полок под
 * ним масштабом обложки и заголовка, а не подложкой.
 */
const PREVIEW_SIZE = 5;

export function Spotlight<T extends string>({
  note,
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
  note?: string;
  title: string;
  facts?: ReactNode;
  actions?: ReactNode;
  art: ReactNode;
  tracks?: Track[];
  href?: Route<T>;
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
        "grid items-end gap-8 max-md:gap-5",
        hasPreview
          ? "grid-cols-[auto_minmax(0,1fr)_minmax(18rem,22rem)] max-xl:grid-cols-[auto_minmax(0,1fr)]"
          : "grid-cols-[auto_minmax(0,1fr)]",
      )}
      aria-labelledby={headingId}
    >
      <div className="size-64 shrink-0 overflow-hidden rounded-xs shadow-art max-md:size-28">
        {art}
      </div>

      <div className="flex min-w-0 flex-col gap-3">
        {note && <p className="text-sm text-muted-foreground">{note}</p>}
        <h2 id={headingId} className="line-clamp-2 font-display text-display">
          {title}
        </h2>
        {facts && <p className="truncate text-muted-foreground">{facts}</p>}
        {actions && <div className="mt-2 flex flex-wrap items-center gap-2.5">{actions}</div>}
      </div>

      {hasPreview && (
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
          {/* На телефоне колонки встают друг под друга, и полный список съедал экран. Режем
              классом, а не срезом массива: очередь по тапу остаётся полной. */}
          <ol aria-label={title} className={capFourOnMobile}>
            {preview.map((track) => (
              <li key={track.id}>
                <SpotlightTrack
                  track={track}
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
  current,
  playing,
  onPlay,
}: {
  track: Track;
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
        "group grid w-full grid-cols-[2.5rem_minmax(0,1fr)_auto] items-center gap-3 rounded-md px-2 py-1.5 text-left",
        "transition-colors duration-150 ease-brand hover:bg-card",
      )}
    >
      <span className="relative size-10 overflow-hidden rounded-xs">
        <TrackCover track={track} className="size-full rounded-none" />
        <PlayBadge size={8} playing={playing} visible={current} className="absolute top-1 left-1" />
      </span>
      <span className="min-w-0">
        <span className={cn("block truncate text-sm font-medium", current && "text-primary")}>
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
