// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { formatArtists } from "@/lib/format";
import type { HomeBlock } from "@/lib/types";
import { useNowPlaying, usePlayerActions, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { PlaylistCover, TrackCover } from "../Cover";
import { PlaylistIcon } from "../Icons";
import { PlayBadge } from "../PlayBadge";
import { Tile } from "@/components/collection/Tile";

export function QuickTiles({ block, origin }: { block: HomeBlock; origin: PlaybackOrigin }) {
  const t = useT();
  const { currentTrackId, isPlaying } = useNowPlaying();
  const player = usePlayerActions();

  const tracks = block.tracks ?? [];
  const playlists = block.playlists ?? [];

  return (
    <>
      {tracks.map((track) => {
        const isCurrent = currentTrackId === track.id;

        return (
          <Tile
            key={track.id}
            current={isCurrent}
            label={track.title}
            sublabel={formatArtists(track)}
            onClick={() => {
              if (isCurrent) {
                player.toggle();
                return;
              }
              player.playTrack(track, tracks, origin);
            }}
            art={<TrackCover track={track} className="size-full rounded-none" />}
            action={<PlayBadge size={8} playing={isCurrent && isPlaying} visible={isCurrent} />}
          />
        );
      })}

      {playlists.map((playlist) => (
        <Tile
          key={playlist.id}
          href={`/playlists/${playlist.id}`}
          label={playlist.name}
          sublabel={t("count.tracks", { count: playlist.trackCount })}
          art={
            <PlaylistCover
              playlist={playlist}
              fallback={<PlaylistIcon size={22} />}
              className="size-full rounded-none"
            />
          }
        />
      ))}
    </>
  );
}
