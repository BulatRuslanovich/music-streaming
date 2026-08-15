"use client";

import { motion, useReducedMotion } from "motion/react";
import Link from "next/link";
import { ReactNode, useEffect, useState } from "react";
import { trackCoverUrl } from "@/lib/media";
import { formatDuration } from "@/lib/format";
import { useCoverColor } from "@/lib/useCoverColor";
import { usePlayer, usePlayerProgress } from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { DURATION, EASE } from "@/lib/motion";
import { ArtistLinks } from "./ArtistLinks";
import { Cover } from "./Cover";
import { Seekbar } from "./Seekbar";
import { LyricsPane } from "./LyricsPane";
import { QueueList } from "./QueuePanel";
import {
  CloseIcon,
  DataSaverIcon,
  HeartIcon,
  LyricsIcon,
  MuteIcon,
  QueueIcon,
  VolumeIcon,
} from "./Icons";

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
  const settings = useSettings();
  const t = useT();
  const [panel, setPanel] = useState<"art" | "queue" | "lyrics">("art");
  const track = player.currentTrack;
  const tint = useCoverColor(trackCoverUrl(track));
  const reduceMotion = useReducedMotion();

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
    <motion.div
      className="fullscreen-player"
      role="dialog"
      aria-modal="true"
      aria-label={t("player.nowPlaying")}
      style={{ ["--cover-tint" as string]: tint ?? "" }}
      initial={reduceMotion ? false : { opacity: 0, y: 24 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: 24 }}
      transition={{ duration: DURATION * 1.5, ease: EASE }}
    >
      <header className="fullscreen-header">
        <button
          type="button"
          className="icon-button"
          onClick={onClose}
          aria-label={t("player.closeFull")}
        >
          <CloseIcon size={22} />
        </button>
        <span className="muted">{t("player.nowPlaying")}</span>
        <div className="fullscreen-header-actions">
          {settings.qualities.length > 1 && (
            <button
              type="button"
              className={`icon-button ${settings.dataSaver ? "is-active" : ""}`}
              onClick={() => settings.update({ dataSaver: !settings.dataSaver })}
              aria-label={t("player.dataSaver")}
              aria-pressed={settings.dataSaver}
            >
              <DataSaverIcon size={20} />
            </button>
          )}
          <button
            type="button"
            className={`icon-button ${panel === "lyrics" ? "is-active" : ""}`}
            onClick={() => setPanel((open) => (open === "lyrics" ? "art" : "lyrics"))}
            aria-label={panel === "lyrics" ? t("lyrics.hide") : t("lyrics.show")}
            aria-pressed={panel === "lyrics"}
            title={t("lyrics.title")}
          >
            <LyricsIcon size={20} />
          </button>

          <button
            type="button"
            className={`icon-button ${panel === "queue" ? "is-active" : ""}`}
            onClick={() => setPanel((open) => (open === "queue" ? "art" : "queue"))}
            aria-label={t("queue.label")}
            aria-pressed={panel === "queue"}
          >
            <QueueIcon size={20} />
          </button>
        </div>
      </header>

      {panel === "queue" ? (
        <div className="fullscreen-queue">
          <QueueList />
        </div>
      ) : panel === "lyrics" ? (
        <div className="fullscreen-lyrics">
          <LyricsPane key={track.id} track={track} />
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
              commitOnRelease
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
              aria-label={
                track.isFavorite ? t("tracks.removeFromFavorites") : t("tracks.addToFavorites")
              }
            >
              <HeartIcon size={22} filled={track.isFavorite} />
            </button>

            <div className="fullscreen-volume">
              <button
                type="button"
                className="icon-button"
                onClick={player.toggleMute}
                aria-label={player.muted ? t("player.unmute") : t("player.mute")}
              >
                {player.muted || player.volume === 0 ? (
                  <MuteIcon size={20} />
                ) : (
                  <VolumeIcon size={20} />
                )}
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
    </motion.div>
  );
}
