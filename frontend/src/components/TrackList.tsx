"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { formatDuration } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import type { Playlist, Track } from "@/lib/types";
import { useAuth } from "@/contexts/AuthContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { ArtistLinks } from "./ArtistLinks";
import { Cover } from "./Cover";
import { EditTrackDialog } from "./EditTrackDialog";
import {
  EditIcon,
  GripIcon,
  HeartIcon,
  MoreIcon,
  PauseIcon,
  PlayIcon,
  PlusIcon,
  QueueIcon,
  TrashIcon,
} from "./Icons";

import { useT } from "@/contexts/I18nContext";

interface TrackListProps {
  tracks: Track[];
  showCover?: boolean;
  showAlbum?: boolean;
  showArtist?: boolean;
  useTrackNumbers?: boolean;
  playedAt?: Record<string, string>;
  onChanged?: () => void;
  playlistId?: string;
  onReorder?: (trackIds: string[]) => void;
  emptyMessage?: string;
}

export function TrackList({
  tracks,
  showCover = true,
  showAlbum = true,
  showArtist = true,
  useTrackNumbers = false,
  playedAt,
  onChanged,
  playlistId,
  onReorder,
  emptyMessage,
}: TrackListProps) {
  const player = usePlayer();
  const { notify, notifyError } = useToast();
  const t = useT();
  const format = useFormat();

  const [menuFor, setMenuFor] = useState<string | null>(null);
  const [favorites, setFavorites] = useState<Record<string, boolean>>({});
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [dropIndex, setDropIndex] = useState<number | null>(null);

  const isFavorite = useCallback(
    (track: Track) => favorites[track.id] ?? track.isFavorite,
    [favorites],
  );

  const toggleFavorite = useCallback(
    async (track: Track) => {
      const next = !isFavorite(track);

      setFavorites((current) => ({ ...current, [track.id]: next }));
      player.patchTrack(track.id, { isFavorite: next });

      try {
        if (next) await api.addFavorite(track.id);
        else await api.removeFavorite(track.id);
      } catch (error) {
        setFavorites((current) => ({ ...current, [track.id]: !next }));
        player.patchTrack(track.id, { isFavorite: !next });
        notifyError(error, t("tracks.favoritesFailed"));
      }
    },
    [isFavorite, notifyError, player, t],
  );

  const play = useCallback(
    (index: number) => {
      const track = tracks[index];
      if (player.currentTrack?.id === track.id) {
        player.toggle();
        return;
      }
      player.playQueue(tracks, index);
    },
    [player, tracks],
  );

  if (tracks.length === 0) {
    return <p className="empty-state">{emptyMessage ?? t("tracks.empty")}</p>;
  }

  return (
    <div className="track-list" role="table">
      <div className="track-row track-head" role="row">
        <span className="track-index" role="columnheader">
          #
        </span>
        <span className="track-main" role="columnheader">
          {t("column.title")}
        </span>
        {showAlbum && (
          <span className="track-album" role="columnheader">
            {t("column.album")}
          </span>
        )}
        {playedAt && (
          <span className="track-date" role="columnheader">
            {t("column.played")}
          </span>
        )}
        <span className="track-actions" role="columnheader" aria-label={t("column.actions")} />
        <span className="track-duration" role="columnheader">
          {t("column.duration")}
        </span>
      </div>

      {tracks.map((track, index) => {
        const isCurrent = player.currentTrack?.id === track.id;
        const isPlayingThis = isCurrent && player.isPlaying;

        return (
          <div
            key={playlistId ? `${track.id}-${index}` : track.id}
            role="row"
            className={[
              "track-row",
              isCurrent ? "is-current" : "",
              dropIndex === index && dragIndex !== null ? "is-drop-target" : "",
            ]
              .filter(Boolean)
              .join(" ")}
            draggable={Boolean(playlistId && onReorder)}
            onDragStart={() => setDragIndex(index)}
            onDragOver={(event) => {
              if (dragIndex === null) return;
              event.preventDefault();
              setDropIndex(index);
            }}
            onDragEnd={() => {
              if (dragIndex !== null && dropIndex !== null && dragIndex !== dropIndex && onReorder) {
                const reordered = [...tracks];
                const [moved] = reordered.splice(dragIndex, 1);
                reordered.splice(dropIndex, 0, moved);
                onReorder(reordered.map((item) => item.id));
              }
              setDragIndex(null);
              setDropIndex(null);
            }}
            onDoubleClick={() => play(index)}
          >
            <span className="track-index" role="cell">
              {playlistId && onReorder && (
                <span className="drag-handle" aria-hidden="true">
                  <GripIcon size={14} />
                </span>
              )}
              <span className="track-number">
                {useTrackNumbers ? (track.trackNumber ?? index + 1) : index + 1}
              </span>
              <button
                type="button"
                className="track-play"
                onClick={() => play(index)}
                aria-label={
                  isPlayingThis
                    ? t("tracks.pauseNamed", { title: track.title })
                    : t("tracks.playNamed", { title: track.title })
                }
              >
                {isPlayingThis ? <PauseIcon size={14} /> : <PlayIcon size={14} />}
              </button>
            </span>

            <span className="track-main" role="cell">
              {showCover && (
                <Cover
                  albumId={track.albumId}
                  trackId={track.id}
                  hasCover={track.hasCover}
                  name={track.albumTitle ?? track.title}
                  size={40}
                />
              )}
              <span className="track-titles">
                <span className={`track-title ${isCurrent ? "is-current-text" : ""}`}>
                  {track.title}
                </span>
                {showArtist && <ArtistLinks track={track} className="track-artist" />}
              </span>
            </span>

            {showAlbum && (
              <span className="track-album" role="cell">
                {track.albumId ? (
                  <Link href={`/albums/${track.albumId}`}>{track.albumTitle}</Link>
                ) : (
                  <span className="muted">—</span>
                )}
              </span>
            )}

            {playedAt && (
              <span className="track-date" role="cell">
                {playedAt[track.id] ? format.relativeDate(playedAt[track.id]) : ""}
              </span>
            )}

            <span className="track-actions" role="cell">
              <button
                type="button"
                className={`icon-button ${isFavorite(track) ? "is-active" : ""}`}
                onClick={() => void toggleFavorite(track)}
                aria-label={
                  isFavorite(track) ? t("tracks.removeFromFavorites") : t("tracks.addToFavorites")
                }
                aria-pressed={isFavorite(track)}
              >
                <HeartIcon size={16} filled={isFavorite(track)} />
              </button>

              <TrackMenu
                track={track}
                open={menuFor === track.id}
                onOpenChange={(open) => setMenuFor(open ? track.id : null)}
                playlistId={playlistId}
                onChanged={onChanged}
                onQueue={() => {
                  player.addToQueue(track);
                  notify(t("menu.addedToQueue", { title: track.title }), "success");
                }}
              />
            </span>

            <span className="track-duration" role="cell">
              {formatDuration(track.durationSeconds)}
            </span>
          </div>
        );
      })}
    </div>
  );
}

function TrackMenu({
  track,
  open,
  onOpenChange,
  playlistId,
  onChanged,
  onQueue,
}: {
  track: Track;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  playlistId?: string;
  onChanged?: () => void;
  onQueue: () => void;
}) {
  const { notify, notifyError } = useToast();
  const { isAdmin } = useAuth();
  const t = useT();
  const [playlists, setPlaylists] = useState<Playlist[] | null>(null);
  const [editing, setEditing] = useState(false);
  const containerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open || playlists !== null) return;

    api
      .playlists()
      .then(setPlaylists)
      .catch(() => setPlaylists([]));
  }, [open, playlists]);

  useEffect(() => {
    if (!open) return;

    const closeOnOutside = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) onOpenChange(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onOpenChange(false);
    };

    document.addEventListener("mousedown", closeOnOutside);
    document.addEventListener("keydown", closeOnEscape);

    return () => {
      document.removeEventListener("mousedown", closeOnOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open, onOpenChange]);

  const addTo = async (playlist: Playlist) => {
    try {
      await api.addToPlaylist(playlist.id, track.id);
      notify(t("menu.addedToPlaylist", { name: playlist.name }), "success");
      onOpenChange(false);
    } catch (error) {
      notifyError(error, t("menu.addToPlaylistFailed"));
    }
  };

  const removeFromPlaylist = async () => {
    if (!playlistId) return;
    try {
      await api.removeFromPlaylist(playlistId, track.id);
      notify(t("menu.removedFromPlaylist"), "success");
      onOpenChange(false);
      onChanged?.();
    } catch (error) {
      notifyError(error, t("menu.removeFromPlaylistFailed"));
    }
  };

  const deleteTrack = async () => {
    const confirmed = window.confirm(t("menu.confirmDeleteTrack", { title: track.title }));
    if (!confirmed) return;

    try {
      await api.deleteTrack(track.id);
      notify(t("menu.trackDeleted", { title: track.title }), "success");
      onOpenChange(false);
      onChanged?.();
    } catch (error) {
      notifyError(error, t("menu.deleteTrackFailed"));
    }
  };

  return (
    <div className="menu-anchor" ref={containerRef}>
      <button
        type="button"
        className="icon-button"
        onClick={() => onOpenChange(!open)}
        aria-label={t("tracks.moreActions", { title: track.title })}
        aria-expanded={open}
      >
        <MoreIcon size={16} />
      </button>

      {open && (
        <div className="menu" role="menu">
          <button type="button" role="menuitem" onClick={onQueue}>
            <QueueIcon size={16} /> {t("menu.addToQueue")}
          </button>

          {isAdmin && (
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setEditing(true);
                onOpenChange(false);
              }}
            >
              <EditIcon size={16} /> {t("menu.editDetails")}
            </button>
          )}

          <div className="menu-separator" />
          <p className="menu-label">{t("menu.addToPlaylist")}</p>

          {playlists === null && <span className="menu-hint">{t("common.loading")}</span>}
          {playlists?.length === 0 && <span className="menu-hint">{t("menu.noPlaylists")}</span>}
          {playlists?.map((playlist) => (
            <button
              key={playlist.id}
              type="button"
              role="menuitem"
              onClick={() => void addTo(playlist)}
            >
              <PlusIcon size={16} /> {playlist.name}
            </button>
          ))}

          {(playlistId || isAdmin) && <div className="menu-separator" />}

          {playlistId && (
            <button type="button" role="menuitem" onClick={() => void removeFromPlaylist()}>
              <TrashIcon size={16} /> {t("menu.removeFromPlaylist")}
            </button>
          )}

          {isAdmin && (
            <button type="button" role="menuitem" className="is-danger" onClick={() => void deleteTrack()}>
              <TrashIcon size={16} /> {t("menu.deleteFromLibrary")}
            </button>
          )}
        </div>
      )}

      {editing && (
        <EditTrackDialog track={track} onClose={() => setEditing(false)} onSaved={onChanged} />
      )}
    </div>
  );
}
