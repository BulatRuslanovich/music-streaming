"use client";

import Link from "next/link";
import { formatDuration } from "@/lib/format";
import type { Album, Artist, Playlist, Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { Cover } from "./Cover";
import { PauseIcon, PlayIcon, PlaylistIcon } from "./Icons";

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

/** Placeholder blocks shown while a page's data is loading. */
export function Skeleton({ count = 6, variant = "card" }: { count?: number; variant?: "card" | "row" }) {
  return (
    <div className={variant === "card" ? "card-grid" : "skeleton-rows"} aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div key={index} className={`skeleton skeleton-${variant}`} />
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

/** Horizontal strip of tracks, used for the home page shelves. */
export function TrackCardRow({ tracks, context }: { tracks: Track[]; context: Track[] }) {
  const player = usePlayer();

  return (
    <div className="card-grid">
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
    </div>
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
