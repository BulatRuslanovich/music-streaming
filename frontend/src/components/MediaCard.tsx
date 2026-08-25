// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQueryClient, type FetchQueryOptions } from "@tanstack/react-query";
import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import { queries } from "@/lib/queries";
import type { Album, Artist, Playlist, Track } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { AlbumCover, ArtistCover, PlaylistCover, TrackCover } from "./Cover";
import { PlaylistIcon } from "./Icons";
import { PlayBadge } from "./PlayBadge";

function usePrefetch<TData, TKey extends readonly unknown[]>(
  options: FetchQueryOptions<TData, Error, TData, TKey>,
) {
  const client = useQueryClient();
  return () => void client.prefetchQuery(options);
}

export function Card({
  href,
  onClick,
  active = false,
  prefetch,
  cover,
  title,
  subtitle,
  round = false,
  bare = false,
  current = false,
  overlay,
}: {
  href?: string;
  onClick?: () => void;
  active?: boolean;
  prefetch?: () => void;
  cover: ReactNode;
  title: string;
  subtitle: ReactNode;
  round?: boolean;
  bare?: boolean;
  current?: boolean;
  overlay?: ReactNode;
}) {
  const body = (
    <>
      <div
        className={cn(
          "relative mb-2 aspect-square w-full overflow-hidden rounded-md bg-raised",
          round && "rounded-full bg-transparent",
        )}
      >
        {cover}
        {overlay}
      </div>
      <span className={cn("truncate text-sm font-semibold", current && "text-primary")}>
        {title}
      </span>
      <span className="truncate text-xs text-muted-foreground">{subtitle}</span>
    </>
  );

  const shell = cn(
    "group flex min-w-0 flex-col gap-1 rounded-xl p-3 text-left transition-colors duration-150 ease-brand",
    bare
      ? "items-center text-center hover:no-underline hover:[&>span:first-of-type]:text-primary"
      : "bg-card hover:bg-raised hover:no-underline",
    active && "bg-primary-soft hover:bg-primary-soft",
  );

  if (href) {
    return (
      <Link href={href} className={shell} onMouseEnter={prefetch} onFocus={prefetch}>
        {body}
      </Link>
    );
  }

  return (
    <button type="button" onClick={onClick} aria-pressed={active || undefined} className={shell}>
      {body}
    </button>
  );
}

export function AlbumCard({ album }: { album: Album }) {
  const prefetch = usePrefetch(queries.album(album.id));

  return (
    <Card
      href={`/albums/${album.id}`}
      prefetch={prefetch}
      title={album.title}
      subtitle={`${album.artistName}${album.year ? ` · ${album.year}` : ""}`}
      cover={<AlbumCover album={album} className="size-full rounded-none" />}
    />
  );
}

export function ArtistCard({ artist, bare = false }: { artist: Artist; bare?: boolean }) {
  const t = useT();
  const prefetch = usePrefetch(queries.artist(artist.id));

  return (
    <Card
      href={`/artists/${artist.id}`}
      prefetch={prefetch}
      round
      bare={bare}
      title={artist.name}
      subtitle={
        t("count.tracks", { count: artist.trackCount }) +
        (artist.albumCount > 0 ? ` · ${t("count.albums", { count: artist.albumCount })}` : "")
      }
      cover={<ArtistCover artist={artist} className="size-full" />}
    />
  );
}

export function PlaylistCard({ playlist, showOwner }: { playlist: Playlist; showOwner?: boolean }) {
  const t = useT();

  const prefetch = usePrefetch(queries.playlist(playlist.id));

  const tail = showOwner
    ? ` · ${t("playlists.by", { name: playlist.ownerName })}`
    : playlist.durationSeconds > 0
      ? ` · ${formatDuration(playlist.durationSeconds)}`
      : "";

  return (
    <Card
      href={`/playlists/${playlist.id}`}
      prefetch={prefetch}
      title={playlist.name}
      subtitle={t("count.tracks", { count: playlist.trackCount }) + tail}
      cover={
        <PlaylistCover
          playlist={playlist}
          fallback={<PlaylistIcon size={34} />}
          className="size-full rounded-none"
        />
      }
    />
  );
}

export function TrackCards({
  tracks,
  context,
  origin,
}: {
  tracks: Track[];
  context: Track[];
  origin?: PlaybackOrigin;
}) {
  const player = usePlayer();

  return (
    <>
      {tracks.map((track) => {
        const isCurrent = player.currentTrack?.id === track.id;

        return (
          <Card
            key={track.id}
            current={isCurrent}
            title={track.title}
            subtitle={formatArtists(track)}
            onClick={() => {
              if (isCurrent) {
                player.toggle();
                return;
              }
              player.playTrack(track, context, origin);
            }}
            cover={<TrackCover track={track} className="size-full rounded-none" />}
            overlay={
              <PlayBadge
                playing={isCurrent && player.isPlaying}
                visible={isCurrent}
                className="absolute right-2 bottom-2"
              />
            }
          />
        );
      })}
    </>
  );
}
