// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQueryClient, type FetchQueryOptions } from "@tanstack/react-query";
import Link from "next/link";
import { useState, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists, formatDuration } from "@/lib/format";
import { queries } from "@/lib/queries";
import type { Album, Artist, Playlist, Track } from "@/lib/types";
import { usePlayback } from "@/lib/usePlayback";
import { useNowPlaying, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
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
  action,
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
  /** Декоративный значок поверх обложки — годится только внутри карточки-кнопки. */
  overlay?: ReactNode;
  /** Кликабельное действие поверх обложки. Ссылку в ссылку вложить нельзя, поэтому
   *  оно рендерится соседом <Link>, а не внутри него. */
  action?: ReactNode;
}) {
  const body = (
    <>
      <div
        className={cn(
          "relative mb-2 aspect-square w-full overflow-hidden rounded-md bg-raised",
          "motion-safe:group-hover:[&_img]:scale-[1.03]",
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
    "flex min-w-0 flex-col gap-1 rounded-xl p-3 text-left transition-colors duration-150 ease-brand",
    bare
      ? "items-center text-center hover:no-underline hover:[&>span:first-of-type]:text-primary"
      : // group-hover, а не только hover: кнопка play лежит снаружи ссылки, и без этого
        // наведение прямо на неё оставляло бы карточку неподсвеченной.
        "bg-card group-hover:bg-raised hover:no-underline",
    active && "bg-primary-soft group-hover:bg-primary-soft",
  );

  if (href) {
    return (
      <div className="group relative flex min-w-0 flex-col">
        <Link
          href={href}
          className={cn(shell, "flex-1")}
          onMouseEnter={prefetch}
          onFocus={prefetch}
        >
          {body}
        </Link>

        {action && (
          // Геометрия повторяет коробку обложки: те же p-3 и aspect-square.
          <div className="pointer-events-none absolute top-3 right-3 left-3 aspect-square">
            {action}
          </div>
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

/**
 * Кнопка запуска поверх обложки карточки-ссылки. Треки подтягиваются по клику: к этому
 * моменту тот же запрос обычно уже лежит в кэше после префетча по наведению.
 */
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
  const { notifyError } = useToast();
  const [busy, setBusy] = useState(false);

  const play = async () => {
    if (busy) return;

    setBusy(true);
    try {
      // Треки известны только после загрузки, поэтому решение «пауза или play» принимает
      // playSet уже с ними на руках — то же правило, что и у кнопки на странице альбома.
      playSet(await load());
    } catch (error) {
      notifyError(error, t("error.load"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <button
      type="button"
      onClick={() => void play()}
      disabled={busy}
      aria-label={playing ? t("action.pause") : t("action.playNamed", { name })}
      className="pointer-events-auto absolute right-2 bottom-2 rounded-full"
    >
      <PlayBadge playing={playing} visible={playing} />
    </button>
  );
}

export function AlbumCard({ album }: { album: Album }) {
  const client = useQueryClient();
  const { currentAlbumId, isPlaying } = useNowPlaying();
  const prefetch = usePrefetch(queries.album(album.id));

  const playing = isPlaying && currentAlbumId === album.id;

  return (
    <Card
      href={`/albums/${album.id}`}
      prefetch={prefetch}
      title={album.title}
      subtitle={`${album.artistName}${album.year ? ` · ${album.year}` : ""}`}
      cover={<AlbumCover album={album} className="size-full rounded-none" />}
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
  const client = useQueryClient();

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
      action={
        // У плейлиста нет признака «сейчас играет» в треке, поэтому иконка всегда play;
        // сам клик по уже играющей очереди всё равно распознаётся и ставит паузу.
        <CardPlayButton
          name={playlist.name}
          playing={false}
          load={async () => (await client.fetchQuery(queries.playlist(playlist.id))).tracks}
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
  const { currentTrackId, playTrack, soundingNow } = usePlayback(origin);

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
            cover={<TrackCover track={track} className="size-full rounded-none" />}
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
