"use client";

import Link from "next/link";
import {ReactNode, useEffect, useState} from "react";
import { trackCoverUrl } from "@/lib/media";
import { formatDuration } from "@/lib/format";
import { useCoverColor } from "@/lib/useCoverColor";
import { usePlayer, usePlayerProgress } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { ArtistLinks } from "./ArtistLinks";
import { Cover } from "./Cover";
import { Seekbar } from "./Seekbar";
import { QueueList } from "./QueuePanel";
import { CloseIcon, DataSaverIcon, HeartIcon, MuteIcon, QueueIcon, VolumeIcon } from "./Icons";

export function FullScreenPlayer({
  onClose,
  transport,
  onToggleFavorite,
}: {
  onClose: () => void;
  transport: ReactNode;
  onToggleFavorite: () => void;
}) {
  const player = usePlayer();
  const progress = usePlayerProgress();
  const t = useT();
  const [showQueue, setShowQueue] = useState(false);
  const track = player.currentTrack;
  const tint = useCoverColor(trackCoverUrl(track));

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  if (!track) return null;

  const duration = progress.duration || track.durationSeconds;

  return (
    <div
      className="fullscreen-player"
      role="dialog"
      aria-modal="true"
      aria-label={t("player.nowPlaying")}
      style={{ ["--cover-tint" as string]: tint ?? "" }}
    >
      <header className="fullscreen-header">
        <button type="button" className="icon-button" onClick={onClose} aria-label={t("player.closeFull")}>
          <CloseIcon size={22} />
        </button>
        <span className="muted">{t("player.nowPlaying")}</span>
        <div className="fullscreen-header-actions">
          {player.dataSaverAvailable && (
            <button
              type="button"
              className={`icon-button ${player.dataSaver ? "is-active" : ""}`}
              onClick={player.toggleDataSaver}
              aria-label={t("player.dataSaver")}
              aria-pressed={player.dataSaver}
            >
              <DataSaverIcon size={20} />
            </button>
          )}
          <button
            type="button"
            className={`icon-button ${showQueue ? "is-active" : ""}`}
            onClick={() => setShowQueue((open) => !open)}
            aria-label={t("queue.label")}
          >
            <QueueIcon size={20} />
          </button>
        </div>
      </header>

      {showQueue ? (
        <div className="fullscreen-queue">
          <QueueList />
        </div>
      ) : (
        <div className="fullscreen-body">
          <div className="fullscreen-art">
            <Cover
              albumId={track.albumId}
              trackId={track.id}
              hasCover={track.hasCover}
              name={track.albumTitle ?? track.title}
              size="100%"
              variant="full"
            />
          </div>

          <div className="fullscreen-meta">
            <h2>{track.title}</h2>
            <ArtistLinks track={track} onNavigate={onClose} />
            {track.albumId && (
              <Link href={`/albums/${track.albumId}`} className="muted" onClick={onClose}>
                {track.albumTitle}
              </Link>
            )}
          </div>

          <div className="fullscreen-progress">
            <Seekbar
              value={progress.position}
              max={duration}
              onSeek={player.seek}
              ariaLabel={t("player.seek")}
            />
            <div className="fullscreen-times">
              <span>{formatDuration(progress.position)}</span>
              <span>{formatDuration(duration)}</span>
            </div>
          </div>

          {transport}

          <div className="fullscreen-extra">
            <button
              type="button"
              className={`icon-button ${track.isFavorite ? "is-active" : ""}`}
              onClick={onToggleFavorite}
              aria-label={track.isFavorite ? t("tracks.removeFromFavorites") : t("tracks.addToFavorites")}
            >
              <HeartIcon size={22} filled={track.isFavorite} />
            </button>

            <div className="fullscreen-volume">
              <button type="button" className="icon-button" onClick={player.toggleMute} aria-label={player.muted ? t("player.unmute") : t("player.mute")}>
                {player.muted || player.volume === 0 ? <MuteIcon size={20} /> : <VolumeIcon size={20} />}
              </button>
              <Seekbar
                value={player.muted ? 0 : player.volume}
                max={1}
                step={0.01}
                onSeek={player.setVolume}
                ariaLabel={t("player.volume")}
              />
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

