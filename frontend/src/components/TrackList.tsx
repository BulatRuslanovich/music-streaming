"use client";

import Link from "next/link";
import { useCallback, useRef, useState } from "react";
import { api } from "@/lib/api";
import { recordEvent } from "@/lib/events";
import { formatDuration } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import type { Playlist, Track } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
import { ArtistLinks } from "./ArtistLinks";
import { Cover } from "./Cover";
import { TrackMenu } from "./TrackMenu";
import { GripIcon, HeartIcon, PauseIcon, PlayIcon } from "./Icons";

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
  origin?: PlaybackOrigin;
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
  origin,
}: TrackListProps) {
  const player = usePlayer();
  const { notify, notifyError } = useToast();
  const t = useT();
  const format = useFormat();

  const [menuFor, setMenuFor] = useState<string | null>(null);
  const [favorites, setFavorites] = useState<Record<string, boolean>>({});
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [dropIndex, setDropIndex] = useState<number | null>(null);

  const [renderedTracks, setRenderedTracks] = useState(tracks);
  if (tracks !== renderedTracks) {
    setRenderedTracks(tracks);
    setFavorites({});
  }

  const playlistsRequest = useRef<Promise<Playlist[]> | null>(null);
  const loadPlaylists = useCallback(() => {
    playlistsRequest.current ??= api.playlists().catch(() => []);
    return playlistsRequest.current;
  }, []);

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

        recordEvent({ type: next ? "trackLiked" : "trackUnliked", trackId: track.id });
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
      player.playQueue(tracks, index, origin);
    },
    [player, tracks, origin],
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
              if (
                dragIndex !== null &&
                dropIndex !== null &&
                dragIndex !== dropIndex &&
                onReorder
              ) {
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
                loadPlaylists={loadPlaylists}
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
