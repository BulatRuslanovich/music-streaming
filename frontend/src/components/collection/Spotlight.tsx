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
import { Overline } from "@/components/ui/label";

/**
 * Два масштаба одной разметки.
 *
 * `compact` — топ-результат поиска: он один из нескольких блоков выдачи и не должен спорить
 * с ними за внимание. `feature` — микс дня на главной, единственный якорь страницы, у которой
 * своей шапки нет: за ним идут только полки на такой же `bg-card`, и отличить его от них можно
 * лишь масштабом. Цветом — нельзя: главная уже красится `TintScrim` по играющему треку, и
 * второе цветовое поле от обложки микса дало бы ровно ту муть, ради которой `DetailHero`
 * гасит подложку через `data-hero`.
 *
 * Шесть треков в `feature` — не «побольше»: при арте в 280px левая колонка занимает ~344px,
 * и четыре строки оставляли под собой пустую полосу `bg-raised` почти в половину высоты.
 */
const PREVIEW_SIZE = { compact: 4, feature: 6 } as const;

type SpotlightSize = keyof typeof PREVIEW_SIZE;

/** Общая плоская подложка для геро-блоков: Spotlight и топ-результата поиска. */
export const heroSurface = "overflow-hidden rounded-xl bg-card";

export function Spotlight<T extends string>({
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
  size = "compact",
}: {
  eyebrow?: string;
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
  size?: SpotlightSize;
}) {
  const t = useT();

  const feature = size === "feature";

  const preview = tracks?.slice(0, PREVIEW_SIZE[size]) ?? [];
  const hasPreview = preview.length > 0 && onPlayTrack !== undefined;

  return (
    <section
      className={cn(
        "grid shrink-0",
        heroSurface,
        // «Дальше» получает фиксированную долю, а не остаток: на 1920px левая колонка
        // раздувалась до ~1100px под обложку и три строки текста, и между ними зияла дыра.
        //
        // Порог по границе планшетной полосы, а не по 1024: на 1100px левой колонке
        // оставалось около 400px под кнопки, и «Воспроизвести» с «Вперемешку» вставали
        // друг под друга разной ширины.
        hasPreview
          ? "grid-cols-[minmax(0,1fr)_minmax(22rem,26rem)] max-xl:grid-cols-1"
          : "grid-cols-1",
      )}
      aria-labelledby={headingId}
    >
      <div
        className={cn(
          "flex min-w-0 items-center gap-6 max-md:items-start max-md:gap-4 max-md:p-4",
          feature ? "p-8" : "p-6",
        )}
      >
        <div
          className={cn(
            "shrink-0 overflow-hidden rounded-xl max-md:size-28",
            feature ? "size-70 shadow-hero" : "size-44 shadow-art",
          )}
        >
          {art}
        </div>

        <div className="min-w-0">
          {eyebrow && <Overline>{eyebrow}</Overline>}
          <h2
            id={headingId}
            className={cn("mt-2 truncate font-bold", feature ? "text-display" : "text-title")}
          >
            {title}
          </h2>
          {facts && <p className="mt-1 truncate text-muted-foreground">{facts}</p>}
          {actions && <div className="mt-5 flex flex-wrap items-center gap-3">{actions}</div>}
        </div>
      </div>

      {hasPreview && (
        <div className="bg-raised p-3">
          <div className="flex items-center justify-between gap-3 px-2 py-1.5">
            <Overline className="truncate">{t("home.upNext")}</Overline>
            {href && (
              <Link
                href={href}
                className="text-xs font-medium text-faint transition-colors duration-150 ease-brand hover:text-foreground hover:no-underline"
              >
                {t("action.seeAll")}
              </Link>
            )}
          </div>
          {/*
            Шесть строк уравнивают колонки только там, где колонки две. На телефоне они встают
            друг под друга, и те же шесть съедали весь экран: до «Включить станцию» приходилось
            листать. Режем классом, а не срезом массива, — по той же причине, что чарт и сетку
            новинок (см. layout.ts): очередь по тапу остаётся полной.
          */}
          <ol aria-label={title} className={cn(feature && capFourOnMobile)}>
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
