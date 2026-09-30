// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { Route } from "next";
import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import { queries } from "@/lib/queries";
import type { Album, Artist, Playlist, Track } from "@/lib/types";
import { usePlayback } from "@/lib/playback/usePlayback";
import { useNowPlaying } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { AlbumCover, ArtistCover, PlaylistCover, TrackCover } from "./Cover";
import { ListMusicIcon } from "lucide-react";
import { PlayBadge } from "./PlayBadge";

export function Card<T extends string>({
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
  action,
}: {
  href?: Route<T>;
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
  action?: ReactNode;
}) {
  const body = (
    <>
      <div
        className={cn(
          "relative mb-2.5 aspect-square w-full overflow-hidden rounded-xs bg-accent shadow-art",
          round && "rounded-full shadow-none",
        )}
      >
        {cover}
        {overlay}
      </div>
      <span
        className={cn(
          "line-clamp-2 text-sm leading-snug font-medium",
          bare && "line-clamp-1",
          current && "text-primary",
        )}
      >
        {title}
      </span>
      <span className="truncate text-sm text-muted-foreground">{subtitle}</span>
    </>
  );

  const shell = cn(
    "flex min-w-0 flex-col gap-0.5 text-left hover:no-underline",
    bare && "items-center text-center",
    active && "[&>div:first-child]:ring-2 [&>div:first-child]:ring-primary",
  );

  if (href) {
    return (
      <div className="group relative flex min-w-0 flex-col">
        <Link
          href={href}
          prefetch={false}
          className={cn(shell, "flex-1")}
          onMouseEnter={prefetch}
          onFocus={prefetch}
        >
          {body}
        </Link>

        {action && (
          <div className="pointer-events-none absolute inset-x-0 top-0 aspect-square">{action}</div>
        )}
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active || undefined}
      className={cn("group", shell)}
    >
      {body}
    </button>
  );
}

export function AlbumCard({ album }: { album: Album }) {
  const client = useQueryClient();
  const { currentAlbumId, isPlaying } = useNowPlaying();
  const prefetch = () => void client.prefetchQuery(queries.album(album.id));

  const playing = isPlaying && currentAlbumId === album.id;

  return (
    <Card
      href={`/albums/${album.id}`}
      prefetch={prefetch}
      title={album.title}
      subtitle={album.year ? `${album.artistName}, ${album.year}` : album.artistName}
      cover={<AlbumCover album={album} />}
      action={
        <CardPlayButton
          name={album.title}
          playing={playing}
          load={async () => (await client.fetchQuery(queries.album(album.id))).tracks}
        />
      }
    />
  );
}

export function ArtistCard({ artist, bare = false }: { artist: Artist; bare?: boolean }) {
  const t = useT();
  const client = useQueryClient();
  const prefetch = () => void client.prefetchQuery(queries.artist(artist.id));

  return (
    <Card
      href={`/artists/${artist.id}`}
      prefetch={prefetch}
      round
      bare={bare}
      title={artist.name}
      subtitle={
        t("count.tracks", { count: artist.trackCount }) +
        (artist.albumCount > 0 ? `, ${t("count.albums", { count: artist.albumCount })}` : "")
      }
      cover={<ArtistCover artist={artist} />}
    />
  );
}

export function PlaylistCard({ playlist, showOwner }: { playlist: Playlist; showOwner?: boolean }) {
  const t = useT();
  const client = useQueryClient();
  const prefetch = () => void client.prefetchQuery(queries.playlist(playlist.id));

  const tail = showOwner
    ? `, ${t("playlists.by", { name: playlist.ownerName })}`
    : playlist.durationSeconds > 0
      ? `, ${formatDuration(playlist.durationSeconds)}`
      : "";

  return (
    <Card
      href={`/playlists/${playlist.id}`}
      prefetch={prefetch}
      title={playlist.name}
      subtitle={t("count.tracks", { count: playlist.trackCount }) + tail}
      cover={<PlaylistCover playlist={playlist} fallback={<ListMusicIcon size={34} />} />}
      action={
        <CardPlayButton
          name={playlist.name}
          playing={false}
          load={async () => (await client.fetchQuery(queries.playlist(playlist.id))).tracks}
        />
      }
    />
  );
}

export function TrackCards({ tracks, context }: { tracks: Track[]; context: Track[] }) {
  const { currentTrackId, playTrack, soundingNow } = usePlayback();

  return (
    <>
      {tracks.map((track) => {
        const isCurrent = currentTrackId === track.id;

        return (
          <Card
            key={track.id}
            current={isCurrent}
            title={track.title}
            subtitle={formatArtists(track)}
            onClick={() => playTrack(track, context)}
            cover={<TrackCover track={track} />}
            overlay={
              <PlayBadge
                playing={soundingNow(track.id)}
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

function CardPlayButton({
  name,
  playing,
  load,
}: {
  name: string;
  playing: boolean;
  load: () => Promise<Track[]>;
}) {
  const t = useT();
  const { playSet } = usePlayback();
  const play = useMutation({ mutationFn: load, onSuccess: (tracks) => playSet(tracks) });

  return (
    <button
      type="button"
      onClick={() => play.mutate()}
      disabled={play.isPending}
      aria-label={playing ? t("action.pause") : t("action.playNamed", { name })}
      className="pointer-events-auto absolute right-2.5 bottom-2.5 rounded-full"
    >
      <PlayBadge playing={playing} visible={playing} standalone />
    </button>
  );
}
