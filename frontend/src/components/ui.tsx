"use client";

import { motion, useReducedMotion } from "motion/react";
import Link from "next/link";
import { ComponentPropsWithoutRef, ReactNode, useEffect, useRef, useState } from "react";
import { formatArtists, formatDuration } from "@/lib/format";
import { scrollContentToTop } from "@/lib/scroll";
import type { Album, Artist, Paged, Playlist, Track } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Cover } from "./Cover";
import { ChevronLeftIcon, ChevronRightIcon, PauseIcon, PlayIcon, PlaylistIcon } from "./Icons";

/**
 * Play/pause buttons across the player, shelves and track rows share this press feedback so
 * the "this is playback control" affordance feels consistent everywhere it appears.
 */
export function PressButton({
  type = "button",
  ...props
}: ComponentPropsWithoutRef<typeof motion.button>) {
  const reduceMotion = useReducedMotion();

  return (
    <motion.button
      type={type}
      whileTap={reduceMotion ? undefined : { scale: 0.92 }}
      transition={{ duration: 0.1 }}
      {...props}
    />
  );
}

export function PageHeader({
  title,
  subtitle,
  actions,
}: {
  title: string;
  subtitle?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="page-header">
      <div>
        <h1>{title}</h1>
        {subtitle && <p className="page-subtitle">{subtitle}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </header>
  );
}

export function SectionHeader({ title, href }: { title: string; href?: string }) {
  const t = useT();

  return (
    <div className="section-header">
      <h2>{title}</h2>
      {href && (
        <Link href={href} className="text-button">
          {t("action.seeAll")}
        </Link>
      )}
    </div>
  );
}

export function LoadError({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const t = useT();

  return (
    <div className="load-error" role="alert">
      <p>{message}</p>
      {onRetry && (
        <button type="button" className="button" onClick={onRetry}>
          {t("action.tryAgain")}
        </button>
      )}
    </div>
  );
}

export function ShelfSection({
  title,
  href,
  children,
}: {
  title: string;
  href?: string;
  children: ReactNode;
}) {
  const t = useT();
  const shelf = useRef<HTMLDivElement>(null);
  const [atStart, setAtStart] = useState(true);
  const [atEnd, setAtEnd] = useState(true);

  useEffect(() => {
    const element = shelf.current;
    if (!element) return;

    const update = () => {
      const furthest = element.scrollWidth - element.clientWidth;
      setAtStart(element.scrollLeft <= 1);
      setAtEnd(element.scrollLeft >= furthest - 1);
    };

    element.addEventListener("scroll", update, { passive: true });

    const observer = new ResizeObserver(update);
    observer.observe(element);

    return () => {
      element.removeEventListener("scroll", update);
      observer.disconnect();
    };
  }, []);

  const scrollShelf = (direction: 1 | -1) => {
    const element = shelf.current;
    if (!element) return;
    element.scrollBy({ left: direction * element.clientWidth * 0.8, behavior: "smooth" });
  };

  return (
    <section>
      <div className="section-header">
        <h2>{title}</h2>

        <div className="section-tools">
          {href && (
            <Link href={href} className="text-button">
              {t("action.seeAll")}
            </Link>
          )}

          <div className="shelf-nav hide-mobile">
            <button
              type="button"
              className="icon-button"
              onClick={() => scrollShelf(-1)}
              disabled={atStart}
              aria-label={t("shelf.scrollBackwards", { title })}
            >
              <ChevronLeftIcon size={20} />
            </button>
            <button
              type="button"
              className="icon-button"
              onClick={() => scrollShelf(1)}
              disabled={atEnd}
              aria-label={t("shelf.scrollForwards", { title })}
            >
              <ChevronRightIcon size={20} />
            </button>
          </div>
        </div>
      </div>

      <div className="shelf" ref={shelf}>
        {children}
      </div>
    </section>
  );
}

const skeletonLayout = {
  card: "card-grid",
  shelf: "shelf",
  row: "skeleton-rows",
} as const;

export function Skeleton({
  count = 6,
  variant = "card",
}: {
  count?: number;
  variant?: keyof typeof skeletonLayout;
}) {
  const block = variant === "row" ? "skeleton-row" : "skeleton-card";

  return (
    <div className={skeletonLayout[variant]} aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div key={index} className={`skeleton ${block}`} />
      ))}
    </div>
  );
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="empty-panel">
      <h3>{title}</h3>
      {description && <p className="muted">{description}</p>}
      {action}
    </div>
  );
}

/**
 * Full-page state for 404s and the root error boundary — same shape, different icon/copy/actions.
 */
export function StatusPage({
  icon,
  tone = "muted",
  title,
  description,
  actions,
}: {
  icon: ReactNode;
  tone?: "muted" | "danger";
  title: string;
  description?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="status-page">
      <span className={`status-icon ${tone === "danger" ? "status-icon-danger" : ""}`}>{icon}</span>
      <h1>{title}</h1>
      {description && <p className="muted">{description}</p>}
      {actions && <div className="status-actions">{actions}</div>}
    </div>
  );
}

export function PlayAllButton({ tracks, name }: { tracks: Track[]; name?: string }) {
  const t = useT();
  const player = usePlayer();

  const isThisQueue =
    tracks.length > 0 &&
    player.currentTrack !== null &&
    tracks.some((track) => track.id === player.currentTrack?.id);

  const playing = isThisQueue && player.isPlaying;

  return (
    <PressButton
      className="play-all"
      disabled={tracks.length === 0}
      onClick={() => {
        if (isThisQueue) {
          player.toggle();
          return;
        }
        player.playQueue(tracks, 0);
      }}
      aria-label={
        playing ? t("action.pause") : name ? t("action.playNamed", { name }) : t("action.play")
      }
    >
      {playing ? <PauseIcon size={22} /> : <PlayIcon size={22} />}
    </PressButton>
  );
}

export function AlbumCard({ album }: { album: Album }) {
  return (
    <Link href={`/albums/${album.id}`} className="card">
      <div className="card-art">
        <Cover albumId={album.id} hasCover={album.hasCover} name={album.title} />
      </div>
      <span className="card-title">{album.title}</span>
      <span className="card-subtitle">
        {album.artistName}
        {album.year ? ` · ${album.year}` : ""}
      </span>
    </Link>
  );
}

export function ArtistCard({ artist }: { artist: Artist }) {
  const t = useT();

  return (
    <Link href={`/artists/${artist.id}`} className="card">
      <div className="card-art">
        <Cover artistId={artist.id} hasCover={artist.hasImage} name={artist.name} rounded />
      </div>
      <span className="card-title">{artist.name}</span>
      <span className="card-subtitle">
        {t("count.tracks", { count: artist.trackCount })}
        {artist.albumCount > 0 ? ` · ${t("count.albums", { count: artist.albumCount })}` : ""}
      </span>
    </Link>
  );
}

export function PlaylistCard({ playlist, showOwner }: { playlist: Playlist; showOwner?: boolean }) {
  const t = useT();

  return (
    <Link href={`/playlists/${playlist.id}`} className="card">
      <div className="card-art">
        <Cover
          playlistId={playlist.id}
          hasCover={playlist.hasCover}
          coverTrackId={playlist.coverTrackId}
          name={playlist.name}
          fallback={<PlaylistIcon size={34} />}
        />
      </div>
      <span className="card-title">{playlist.name}</span>
      <span className="card-subtitle">
        {t("count.tracks", { count: playlist.trackCount })}
        {showOwner
          ? ` · ${t("playlists.by", { name: playlist.ownerName })}`
          : playlist.durationSeconds > 0
            ? ` · ${formatDuration(playlist.durationSeconds)}`
            : ""}
      </span>
    </Link>
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
          <button
            key={track.id}
            type="button"
            className={`card card-button ${isCurrent ? "is-current" : ""}`}
            onClick={() => {
              if (isCurrent) {
                player.toggle();
                return;
              }
              player.playTrack(track, context, origin);
            }}
          >
            <div className="card-art">
              <Cover
                albumId={track.albumId}
                trackId={track.id}
                hasCover={track.hasCover}
                name={track.albumTitle ?? track.title}
              />
              <span className="card-play" aria-hidden="true">
                {isCurrent && player.isPlaying ? <PauseIcon size={18} /> : <PlayIcon size={18} />}
              </span>
            </div>
            <span className="card-title">{track.title}</span>
            <span className="card-subtitle">{formatArtists(track)}</span>
          </button>
        );
      })}
    </>
  );
}

export function Pagination({
  result,
  onChange,
}: {
  result: Paged<unknown>;
  onChange: (page: number) => void;
}) {
  const t = useT();

  const { page, totalPages } = result;

  if (totalPages <= 1) return null;

  const goTo = (next: number) => {
    onChange(next);
    scrollContentToTop();
  };

  return (
    <nav className="pagination" aria-label={t("pagination.label")}>
      <button type="button" className="button" disabled={page <= 1} onClick={() => goTo(page - 1)}>
        {t("pagination.previous")}
      </button>
      <span className="muted">{t("pagination.pageOf", { page, totalPages })}</span>
      <button
        type="button"
        className="button"
        disabled={page >= totalPages}
        onClick={() => goTo(page + 1)}
      >
        {t("pagination.next")}
      </button>
    </nav>
  );
}
