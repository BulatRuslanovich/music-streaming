// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { formatArtists } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import type { HomeBlock, Track } from "@/lib/types";
import { usePlayback } from "@/lib/usePlayback";
import type { PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Poster, PosterGrid } from "@/components/collection/Poster";
import { TrackCover } from "../Cover";
import { PlayBadge } from "../PlayBadge";
import { Badge } from "../ui/badge";

const FRESH_DAYS = 14;

/**
 * Ниже этого числа плиток сетка выглядит обрывком, и лучше показать треки как есть,
 * чем честную, но пустую полку из одной карточки.
 */
const MIN_TILES = 3;

export function NewArrivalsGrid({ block, origin }: { block: HomeBlock; origin: PlaybackOrigin }) {
  const all = block.tracks ?? [];
  const tracks = byAlbum(all);

  // После пакетного импорта «новое» — это вся библиотека, и бейдж на каждой карточке
  // перестаёт что-либо различать. Показываем его, только если в блоке есть и не новые.
  const fresh = tracks.filter((track) => daysSince(track.createdAt) <= FRESH_DAYS);
  const badgeIsMeaningful = fresh.length > 0 && fresh.length < tracks.length;

  return (
    <PosterGrid>
      {tracks.map((track, index) => (
        <TrackPoster
          key={track.id}
          track={track}
          context={all}
          origin={origin}
          wide={index === 0}
          showFreshBadge={badgeIsMeaningful}
        />
      ))}
    </PosterGrid>
  );
}

/**
 * Один трек на альбом. Блок приходит треками, а импорт сборника даёт их десятками подряд —
 * и первый экран продукта превращался в стену из одной и той же обложки. Сборник теперь
 * представляет одна плитка; остальные его треки никуда не деваются, они на странице полки.
 *
 * Треки без альбома проходят как есть: их нечем схлопывать.
 */
function byAlbum(tracks: Track[]): Track[] {
  const seen = new Set<string>();
  const distinct: Track[] = [];

  for (const track of tracks) {
    if (track.albumId !== null && track.albumId !== undefined) {
      if (seen.has(track.albumId)) continue;
      seen.add(track.albumId);
    }

    distinct.push(track);
  }

  // Вся полка из одного альбома — вырожденный случай: тогда полезнее показать треки.
  return distinct.length >= MIN_TILES ? distinct : tracks;
}

function TrackPoster({
  track,
  context,
  origin,
  wide,
  showFreshBadge,
}: {
  track: Track;
  context: Track[];
  origin: PlaybackOrigin;
  wide: boolean;
  showFreshBadge: boolean;
}) {
  const t = useT();
  const format = useFormat();
  const { currentTrackId, playTrack, soundingNow } = usePlayback(origin);

  const isCurrent = currentTrackId === track.id;
  const isFresh = showFreshBadge && daysSince(track.createdAt) <= FRESH_DAYS;

  return (
    <Poster
      wide={wide}
      onClick={() => playTrack(track, context)}
      cover={
        <TrackCover
          track={track}
          variant={wide ? "full" : "thumb"}
          className="size-full rounded-none"
        />
      }
      badge={
        isFresh && (
          <Badge className="absolute top-3 left-3 bg-black/55 text-white">
            {t("home.newBadge")}
          </Badge>
        )
      }
      overlay={
        <PlayBadge
          playing={soundingNow(track.id)}
          visible={isCurrent}
          className="absolute top-3 right-3"
        />
      }
      title={track.title}
      subtitle={formatArtists(track)}
      footnote={
        wide ? t("home.addedOn", { when: format.relativeDate(track.createdAt) }) : undefined
      }
    />
  );
}

function daysSince(isoDate: string): number {
  const added = new Date(isoDate).getTime();
  if (Number.isNaN(added)) return Number.POSITIVE_INFINITY;

  return (Date.now() - added) / 86_400_000;
}
