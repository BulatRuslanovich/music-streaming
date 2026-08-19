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
import { Cover } from "./Cover";
import { PauseIcon, PlayIcon, PlaylistIcon } from "./Icons";

function usePrefetch<TData, TKey extends readonly unknown[]>(
  options: FetchQueryOptions<TData, Error, TData, TKey>,
) {
  const client = useQueryClient();
  return () => void client.prefetchQuery(options);
}

function Card({
  href,
  onClick,
  prefetch,
  cover,
  title,
  subtitle,
  round = false,
  current = false,
  overlay,
}: {
  href?: string;
  onClick?: () => void;
  prefetch?: () => void;
  cover: ReactNode;
  title: string;
  subtitle: ReactNode;
  round?: boolean;
  current?: boolean;
  overlay?: ReactNode;
}) {
  const body = (
    <>
      <div
        className={cn(
          "relative mb-2 aspect-square w-full overflow-hidden rounded-md bg-raised shadow-art",
          round && "rounded-full bg-transparent shadow-none",
        )}
      >
        {cover}
        {overlay}
      </div>
      <span className={cn("truncate font-semibold", current && "text-primary")}>{title}</span>
      <span className="truncate text-sm text-muted-foreground">{subtitle}</span>
    </>
  );

  const shell = cn(
    "group flex min-w-0 flex-col gap-1 rounded-xl border border-transparent bg-card p-3 text-left transition-[background-color,border-color] duration-150 ease-brand",
    "hover:border-border hover:bg-raised hover:no-underline",
  );

  if (href) {
    return (
      <Link href={href} className={shell} onMouseEnter={prefetch} onFocus={prefetch}>
        {body}
      </Link>
    );
  }

  return (
    <button type="button" onClick={onClick} className={shell}>
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
      cover={
        <Cover
          albumId={album.id}
          hasCover={album.hasCover}
          name={album.title}
          className="size-full rounded-none"
        />
      }
    />
  );
}

export function ArtistCard({ artist }: { artist: Artist }) {
  const t = useT();
  const prefetch = usePrefetch(queries.artist(artist.id));

  return (
    <Card
      href={`/artists/${artist.id}`}
      prefetch={prefetch}
      round
      title={artist.name}
      subtitle={
        t("count.tracks", { count: artist.trackCount }) +
        (artist.albumCount > 0 ? ` · ${t("count.albums", { count: artist.albumCount })}` : "")
      }
      cover={
        <Cover
          artistId={artist.id}
          hasCover={artist.hasImage}
          name={artist.name}
          rounded
          className="size-full"
        />
      }
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
        <Cover
          playlistId={playlist.id}
          hasCover={playlist.hasCover}
          coverTrackId={playlist.coverTrackId}
          name={playlist.name}
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
            cover={
              <Cover
                albumId={track.albumId}
                trackId={track.id}
                hasCover={track.hasCover}
                name={track.albumTitle ?? track.title}
                className="size-full rounded-none"
              />
            }
            overlay={
              <span
                aria-hidden="true"
                className={cn(
                  "absolute right-2 bottom-2 grid size-9 place-items-center rounded-full bg-primary text-primary-foreground shadow-art",
                  "translate-y-1.5 opacity-0 transition-[opacity,transform] duration-150 ease-brand",
                  "group-hover:translate-y-0 group-hover:opacity-100",
                  isCurrent && "translate-y-0 opacity-100",
                )}
              >
                {isCurrent && player.isPlaying ? <PauseIcon size={18} /> : <PlayIcon size={18} />}
              </span>
            }
          />
        );
      })}
    </>
  );
}
