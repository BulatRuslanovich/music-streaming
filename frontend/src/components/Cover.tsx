// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { ReactNode, useCallback, useState } from "react";
import { cn } from "@/lib/cn";
import {
  artistImageSrcSet,
  artistImageUrl,
  coverSrcSet,
  coverUrl,
  playlistCoverSrcSet,
  playlistCoverUrl,
  type CoverVariant,
} from "@/lib/media";
import { initialsFor } from "@/lib/format";
import type { Track } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";

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
  /**
   * Ширина картинки на экране в терминах атрибута `sizes`. Включает `srcset` по всем трём
   * рендишенам — но только здесь: без неё браузер считает картинку шириной во весь вьюпорт
   * и на телефоне тянет 1024 под обложку в 40 пикселей.
   *
   * Нужно там, где арт крупный и по-настоящему разный на разных экранах: полноэкранный
   * плеер, шапка альбома. Полке достаточно одного `variant`.
   */
  sizes?: string;
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
  sizes,
  rounded = false,
  className = "",
  fallback,
}: CoverProps) {
  const [failed, setFailed] = useState(false);
  const [loadedSource, setLoadedSource] = useState<string | null>(null);
  const t = useT();

  const source = artistId
    ? artistImageUrl({ artistId, hasImage: hasCover, variant })
    : playlistId
      ? playlistCoverUrl({ playlistId, hasCover, coverTrackId, variant })
      : coverUrl({ albumId, trackId, hasCover, variant });
  const showImage = source !== null && !failed;

  // Рендишены есть у всех трёх видов картинок, так что srcset собирается одинаково.
  // Прежде фото артиста запрашивалось всегда в полном размере: variant до него не доходил,
  // и сетка на шестьдесят кружков по 64 пикселя тянула шестьдесят файлов по 640.
  const srcSet = !sizes
    ? null
    : artistId
      ? artistImageSrcSet({ artistId, hasImage: hasCover })
      : playlistId
        ? playlistCoverSrcSet({ playlistId, hasCover, coverTrackId })
        : coverSrcSet({ albumId, trackId, hasCover });

  // Смена source сама сбрасывает признак загрузки: сравниваем с тем, что реально проявилось.
  const loaded = source !== null && loadedSource === source;

  const attach = useCallback((image: HTMLImageElement | null) => {
    // Картинка из кэша приходит уже `complete`, и onLoad по ней не сработает.
    // Без этой проверки полка мигала бы при каждой повторной прокрутке.
    if (image?.complete) setLoadedSource(image.getAttribute("src"));
  }, []);

  const style = {
    width: typeof size === "number" ? `${size}px` : size,
    height: typeof size === "number" ? `${size}px` : size,
  };

  return (
    <div
      style={style}
      data-placeholder={showImage ? undefined : "true"}
      className={cn(
        "relative grid shrink-0 place-items-center overflow-hidden bg-accent [container-type:inline-size]",
        // Скругляется только самостоятельная миниатюра с числовым размером. Обложка на 100%
        // заполняет контейнер (карточку, шапку, конверт), и угол у неё — его.
        typeof size === "number" && "rounded-xs",
        rounded && "rounded-full",
        className,
      )}
    >
      {showImage ? (
        <img
          ref={attach}
          src={source!}
          srcSet={srcSet ?? undefined}
          sizes={srcSet ? sizes : undefined}
          alt={t("cover.alt", { name })}
          loading="lazy"
          decoding="async"
          onLoad={() => setLoadedSource(source)}
          onError={() => setFailed(true)}
          className={cn(
            "size-full object-cover",
            // Именно `scale`, а не `transform`: hover-утилита Tailwind v4 пишет отдельное
            // свойство scale, и переход по transform его бы не поймал.
            "[transition:opacity_300ms_var(--ease),scale_150ms_var(--ease)]",
            loaded ? "opacity-100" : "opacity-0",
          )}
        />
      ) : fallback || rounded ? (
        <span
          aria-hidden="true"
          className="grid size-full place-items-center text-[clamp(0.72rem,22cqw,3.25rem)] leading-none font-medium text-faint [&_svg]:size-[38%] [&_svg]:max-h-18 [&_svg]:max-w-18"
        >
          {fallback ?? initialsFor(name)}
        </span>
      ) : (
        <BlankSleeve />
      )}
    </div>
  );
}

/**
 * Конверт без картинки: край пластинки, наклейка и отверстие, всё в долях от ширины, так что
 * одна разметка годится и для миниатюры в строке, и для шапки альбома. Нота во всю обложку
 * кричала громче настоящих обложек рядом; пластинка остаётся тихой и при этом своей.
 */
function BlankSleeve() {
  return (
    <span
      aria-hidden="true"
      className="grid size-[62%] place-items-center rounded-full border border-border-strong"
    >
      <span className="grid size-[36%] place-items-center rounded-full bg-border-strong">
        <span className="size-[22%] rounded-full bg-accent" />
      </span>
    </span>
  );
}

type CoverLook = Omit<
  CoverProps,
  "albumId" | "trackId" | "artistId" | "playlistId" | "coverTrackId" | "hasCover" | "name"
>;

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
