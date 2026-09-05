// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Route } from "next";
import { useQuery, useQueryClient, type FetchQueryOptions } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { TRACK_PAGE_SIZE } from "@/lib/pageSizes";
import { queries } from "@/lib/queries";
import type { Track } from "@/lib/types";
import { usePrefetch } from "@/lib/usePrefetch";
import { useT } from "@/contexts/I18nContext";
import { CardPlayButton } from "@/components/CardPlayButton";
import { Card } from "@/components/MediaCard";
import { HeartIcon, HistoryIcon } from "@/components/Icons";
import { CoverMosaic } from "./CoverMosaic";

// Размер общий с navigationPrefetch и страницами назначения — иначе префетч по наведению и
// загрузка по кнопке запуска промахнулись бы мимо кэша друг друга.
const PAGE = { page: 1, pageSize: TRACK_PAGE_SIZE } as const;

/** Размер иконки-заглушки на обложке карточки — тот же, что у PlaylistCard. */
const ICON = 34;

/**
 * Избранное, недавние и вся фонотека — по сути те же плейлисты, только собранные не руками.
 * Раньше они жили отдельной полосой узких плиток над сеткой плейлистов, и страница читалась
 * как два разных макета подряд. Теперь это первые три ячейки той же сетки.
 */
export function LibraryCards() {
  const t = useT();
  const overview = useQuery(queries.libraryOverview());

  // Одно условие закрывает и «ещё грузится», и пустую фонотеку, где все три ярлыка вели бы в никуда.
  if (!overview.data || overview.data.stats.trackCount === 0) return null;

  const { stats, recentTracks } = overview.data;

  return (
    <>
      <LibraryCard
        href="/favorites"
        title={t("nav.favorites")}
        subtitle={t("count.tracks", { count: stats.favoriteCount })}
        cover={
          <span className="grid size-full place-items-center bg-primary-soft text-primary">
            <HeartIcon size={ICON} filled />
          </span>
        }
        source={queries.favorites(PAGE)}
        tracksOf={(page) => page.items}
      />

      <LibraryCard
        href="/recently-played"
        title={t("nav.recentlyPlayed")}
        subtitle={t("library.fromHistory")}
        cover={
          <span className="grid size-full place-items-center bg-raised text-muted-foreground">
            <HistoryIcon size={ICON} />
          </span>
        }
        source={queries.recentlyPlayed(PAGE)}
        tracksOf={(page) => page.items}
      />

      <LibraryCard
        href="/tracks"
        title={t("library.allTracks")}
        subtitle={t("count.tracks", { count: stats.trackCount })}
        // Обложки недавно добавленного: единственная из трёх карточек с живой картинкой,
        // так что все три различимы с одного взгляда — цветная, нейтральная, из обложек.
        cover={<CoverMosaic tracks={recentTracks} />}
        source={queries.tracks({ ...PAGE, sort: "Title", q: undefined })}
        tracksOf={(page) => page.items}
      />
    </>
  );
}

function LibraryCard<TData, TKey extends readonly unknown[], THref extends string>({
  href,
  title,
  subtitle,
  cover,
  source,
  tracksOf,
}: {
  href: Route<THref>;
  title: string;
  subtitle: ReactNode;
  cover: ReactNode;
  /** Тот же queryOptions, что и у страницы назначения: и префетч, и запуск бьют в один ключ. */
  source: FetchQueryOptions<TData, Error, TData, TKey>;
  tracksOf: (data: TData) => Track[];
}) {
  const client = useQueryClient();
  const prefetch = usePrefetch(source);

  return (
    <Card
      href={href}
      prefetch={prefetch}
      title={title}
      subtitle={subtitle}
      cover={cover}
      action={
        // Признака «играет именно этот набор» здесь нет, поэтому иконка всегда play; клик по
        // уже играющей очереди всё равно распознаётся и ставит паузу — как у PlaylistCard.
        <CardPlayButton
          name={title}
          playing={false}
          load={async () => tracksOf(await client.fetchQuery(source))}
        />
      }
    />
  );
}
