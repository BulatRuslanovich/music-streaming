// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { ReactNode, useState } from "react";
import { cn } from "@/lib/cn";
import { artistImageUrl, coverUrl, playlistCoverUrl, type CoverVariant } from "@/lib/media";
import { accentFor, initialsFor } from "@/lib/format";
import type { Track } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { NoteIcon } from "./Icons";

interface CoverProps {
  albumId?: string | null;
  trackId?: string | null;
  artistId?: string | null;
  playlistId?: string | null;
  coverTrackId?: string | null;
  hasCover?: boolean;
  name: string;
  size?: number | string;
  variant?: CoverVariant;
  rounded?: boolean;
  className?: string;
  fallback?: ReactNode;
}

export function Cover({
  albumId,
  trackId,
  artistId,
  playlistId,
  coverTrackId,
  hasCover = true,
  name,
  size = "100%",
  variant = "thumb",
  rounded = false,
  className = "",
  fallback,
}: CoverProps) {
  const [failed, setFailed] = useState(false);
  const t = useT();

  const source = artistId
    ? artistImageUrl({ artistId, hasImage: hasCover })
    : playlistId
      ? playlistCoverUrl({ playlistId, hasCover, coverTrackId, variant })
      : coverUrl({ albumId, trackId, hasCover, variant });
  const showImage = source !== null && !failed;

  const style = {
    width: typeof size === "number" ? `${size}px` : size,
    height: typeof size === "number" ? `${size}px` : size,
    ...(showImage ? {} : { background: accentFor(name || "?") }),
  };

  return (
    <div
      style={style}
      data-placeholder={showImage ? undefined : "true"}
      className={cn(
        "relative grid shrink-0 place-items-center overflow-hidden rounded-md bg-raised [container-type:inline-size]",
        "data-[placeholder]:shadow-[var(--hairline)]",
        rounded && "rounded-full",
        className,
      )}
    >
      {showImage ? (
        <img
          src={source!}
          alt={t("cover.alt", { name })}
          loading="lazy"
          onError={() => setFailed(true)}
          className="size-full object-cover transition-transform duration-150 ease-brand"
        />
      ) : (
        <span
          aria-hidden="true"
          className="grid size-full place-items-center text-[clamp(0.72rem,30cqw,4.5rem)] leading-none font-bold tracking-wide text-white/85 [&_svg]:size-[38%] [&_svg]:max-h-18 [&_svg]:max-w-18"
        >
          {fallback ?? (rounded ? initialsFor(name) : <NoteIcon size={22} />)}
        </span>
      )}
    </div>
  );
}

/** Всё, кроме того, откуда берётся картинка: подставляет каждая обёртка ниже. */
type CoverLook = Omit<
  CoverProps,
  "albumId" | "trackId" | "artistId" | "playlistId" | "coverTrackId" | "hasCover" | "name"
>;

/** Обложка трека — альбомная, если альбом известен, иначе своя. */
export function TrackCover({
  track,
  ...look
}: {
  track: Pick<Track, "id" | "title" | "albumId" | "albumTitle" | "hasCover">;
} & CoverLook) {
  return (
    <Cover
      albumId={track.albumId}
      trackId={track.id}
      hasCover={track.hasCover}
      name={track.albumTitle ?? track.title}
      {...look}
    />
  );
}

export function AlbumCover({
  album,
  ...look
}: {
  album: { id: string; title: string; hasCover: boolean };
} & CoverLook) {
  return <Cover albumId={album.id} hasCover={album.hasCover} name={album.title} {...look} />;
}

/** Фотография исполнителя всегда круглая. */
export function ArtistCover({
  artist,
  ...look
}: {
  artist: { id: string; name: string; hasImage: boolean };
} & CoverLook) {
  return (
    <Cover artistId={artist.id} hasCover={artist.hasImage} name={artist.name} rounded {...look} />
  );
}

export function PlaylistCover({
  playlist,
  ...look
}: {
  playlist: { id: string; name: string; hasCover: boolean; coverTrackId?: string | null };
} & CoverLook) {
  return (
    <Cover
      playlistId={playlist.id}
      hasCover={playlist.hasCover}
      coverTrackId={playlist.coverTrackId}
      name={playlist.name}
      {...look}
    />
  );
}
