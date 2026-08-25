// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { formatArtists } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import type { HomeBlock, Track } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Poster, PosterGrid } from "@/components/collection/Poster";
import { TrackCover } from "../Cover";
import { PlayBadge } from "../PlayBadge";
import { Badge } from "../ui/badge";

const FRESH_DAYS = 14;

export function NewArrivalsGrid({ block, origin }: { block: HomeBlock; origin: PlaybackOrigin }) {
  const tracks = block.tracks ?? [];

  return (
    <PosterGrid>
      {tracks.map((track, index) => (
        <TrackPoster
          key={track.id}
          track={track}
          context={tracks}
          origin={origin}
          wide={index === 0}
        />
      ))}
    </PosterGrid>
  );
}

function TrackPoster({
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
    <Poster
      wide={wide}
      onClick={() => {
        if (isCurrent) {
          player.toggle();
          return;
        }
        player.playTrack(track, context, origin);
      }}
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
          playing={isCurrent && player.isPlaying}
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
