"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { formatDuration } from "@/lib/format";
import type { Album, Artist, Playlist, Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { Cover } from "./Cover";
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  PauseIcon,
  PlayIcon,
  PlaylistIcon,
} from "./Icons";

/** Page header with an optional subtitle and right-aligned actions. */
export function PageHeader({
  title,
  subtitle,
  actions,
}: {
  title: string;
  subtitle?: React.ReactNode;
  actions?: React.ReactNode;
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
  return (
    <div className="section-header">
      <h2>{title}</h2>
      {href && (
        <Link href={href} className="text-button">
          See all
        </Link>
      )}
    </div>
  );
}

export function LoadError({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="load-error" role="alert">
      <p>{message}</p>
      {onRetry && (
        <button type="button" className="button" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}

/**
 * A home-page section: a heading, an optional link to the full list, and one row of tiles that
 * scrolls sideways instead of wrapping. Library listings use `.card-grid`, which wraps, because
 * there everything is meant to be visible at once.
 *
 * The arrows are the desktop affordance — a trackpad or a finger can swipe the row directly, but a
 * mouse has nothing to grab — so they are hidden on phones.
 */
export function ShelfSection({
  title,
  href,
  children,
}: {
  title: string;
  href?: string;
  children: React.ReactNode;
}) {
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

    // A ResizeObserver reports the element's size as soon as it starts observing, so this doubles
    // as the first measurement — the arrows never need to be measured during the effect itself.
    const observer = new ResizeObserver(update);
    observer.observe(element);

    return () => {
      element.removeEventListener("scroll", update);
      observer.disconnect();
    };
  }, []);

  /** Scrolls by most of a screenful, leaving a tile of overlap for orientation. */
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
              See all
            </Link>
          )}

          <div className="shelf-nav hide-mobile">
            <button
              type="button"
              className="icon-button"
              onClick={() => scrollShelf(-1)}
              disabled={atStart}
              aria-label={`Scroll ${title} backwards`}
            >
              <ChevronLeftIcon size={20} />
            </button>
            <button
              type="button"
              className="icon-button"
              onClick={() => scrollShelf(1)}
              disabled={atEnd}
              aria-label={`Scroll ${title} forwards`}
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

/** Placeholder blocks shown while a page's data is loading, laid out like the real content. */
export function Skeleton({
  count = 6,
  variant = "card",
}: {
  count?: number;
  variant?: keyof typeof skeletonLayout;
}) {
  // A shelf tile is the same shape as a grid tile; only the container differs.
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
  action?: React.ReactNode;
}) {
  return (
    <div className="empty-panel">
      <h3>{title}</h3>
      {description && <p className="muted">{description}</p>}
      {action}
    </div>
  );
}

/** Big round play button used on album, artist and playlist headers. */
export function PlayAllButton({
  tracks,
  label = "Play",
}: {
  tracks: Track[];
  label?: string;
}) {
  const player = usePlayer();

  const isThisQueue =
    tracks.length > 0 &&
    player.currentTrack !== null &&
    tracks.some((track) => track.id === player.currentTrack?.id);

  const playing = isThisQueue && player.isPlaying;

  return (
    <button
      type="button"
      className="play-all"
      disabled={tracks.length === 0}
      onClick={() => {
        if (isThisQueue) {
          player.toggle();
          return;
        }
        player.playQueue(tracks, 0);
      }}
      aria-label={playing ? "Pause" : label}
    >
      {playing ? <PauseIcon size={22} /> : <PlayIcon size={22} />}
    </button>
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
  return (
    <Link href={`/artists/${artist.id}`} className="card">
      <div className="card-art">
        <Cover name={artist.name} hasCover={false} rounded />
      </div>
      <span className="card-title">{artist.name}</span>
      <span className="card-subtitle">
        {artist.trackCount} track{artist.trackCount === 1 ? "" : "s"}
        {artist.albumCount > 0 ? ` · ${artist.albumCount} album${artist.albumCount === 1 ? "" : "s"}` : ""}
      </span>
    </Link>
  );
}

export function PlaylistCard({ playlist }: { playlist: Playlist }) {
  return (
    <Link href={`/playlists/${playlist.id}`} className="card">
      <div className="card-art card-art-playlist">
        <PlaylistIcon size={34} />
      </div>
      <span className="card-title">{playlist.name}</span>
      <span className="card-subtitle">
        {playlist.trackCount} track{playlist.trackCount === 1 ? "" : "s"}
        {playlist.durationSeconds > 0 ? ` · ${formatDuration(playlist.durationSeconds)}` : ""}
      </span>
    </Link>
  );
}

/** Track tiles for a home-page shelf; the surrounding ShelfSection lays them out. */
export function TrackCards({ tracks, context }: { tracks: Track[]; context: Track[] }) {
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
              player.playTrack(track, context);
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
            <span className="card-subtitle">{track.artistName}</span>
          </button>
        );
      })}
    </>
  );
}

/** Previous/next pager for the long, paged library lists. */
export function Pagination({
  page,
  totalPages,
  onChange,
}: {
  page: number;
  totalPages: number;
  onChange: (page: number) => void;
}) {
  if (totalPages <= 1) return null;

  return (
    <nav className="pagination" aria-label="Pagination">
      <button type="button" className="button" disabled={page <= 1} onClick={() => onChange(page - 1)}>
        Previous
      </button>
      <span className="muted">
        Page {page} of {totalPages}
      </span>
      <button
        type="button"
        className="button"
        disabled={page >= totalPages}
        onClick={() => onChange(page + 1)}
      >
        Next
      </button>
    </nav>
  );
}
