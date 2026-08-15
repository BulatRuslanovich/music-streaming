"use client";

import { AnimatePresence } from "motion/react";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { recordEvent } from "@/lib/events";
import { trackCoverUrl } from "@/lib/media";
import { formatDuration } from "@/lib/format";
import type { TranslationKey } from "@/lib/i18n";
import { useCoverColor } from "@/lib/useCoverColor";
import { usePlayer, usePlayerProgress, type RepeatMode } from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { ArtistLinks } from "./ArtistLinks";
import { Cover } from "./Cover";
import { Seekbar } from "./Seekbar";
import { FullScreenPlayer } from "./FullScreenPlayer";
import { QueuePanel } from "./QueuePanel";
import { PressButton } from "./ui";
import {
  ChevronUpIcon,
  DataSaverIcon,
  HeartIcon,
  MuteIcon,
  NextIcon,
  PauseIcon,
  PlayIcon,
  PreviousIcon,
  QueueIcon,
  RepeatIcon,
  RepeatOneIcon,
  ShuffleIcon,
  VolumeIcon,
} from "./Icons";

const REPEAT_MODES: Record<RepeatMode, TranslationKey> = {
  off: "player.repeatOff",
  one: "player.repeatOne",
  all: "player.repeatAll",
};

export function Player() {
  const player = usePlayer();
  const progress = usePlayerProgress();
  const settings = useSettings();
  const { notifyError } = useToast();
  const t = useT();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const { currentTrack } = player;

  const tint = useCoverColor(trackCoverUrl(currentTrack, "thumb"));

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const isTyping =
        target?.tagName === "INPUT" ||
        target?.tagName === "TEXTAREA" ||
        target?.isContentEditable === true;

      if (isTyping || !currentTrack) return;

      switch (event.key) {
        case " ":
          event.preventDefault();
          player.toggle();
          break;
        case "ArrowRight":
          if (event.shiftKey) {
            event.preventDefault();
            player.next();
          } else if (event.altKey) {
            event.preventDefault();
            player.seekBy(10);
          }
          break;
        case "ArrowLeft":
          if (event.shiftKey) {
            event.preventDefault();
            player.previous();
          } else if (event.altKey) {
            event.preventDefault();
            player.seekBy(-10);
          }
          break;
        default:
          break;
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [player, currentTrack]);

  const toggleFavorite = async () => {
    if (!currentTrack) return;
    const next = !currentTrack.isFavorite;

    player.patchTrack(currentTrack.id, { isFavorite: next });
    try {
      if (next) await api.addFavorite(currentTrack.id);
      else await api.removeFavorite(currentTrack.id);

      recordEvent({ type: next ? "trackLiked" : "trackUnliked", trackId: currentTrack.id });
    } catch (error) {
      player.patchTrack(currentTrack.id, { isFavorite: !next });
      notifyError(error, t("tracks.favoritesFailed"));
    }
  };

  if (!currentTrack) {
    return (
      <footer className="player player-idle">
        <p className="muted">{t("player.idle")}</p>
      </footer>
    );
  }

  const duration = progress.duration || currentTrack.durationSeconds;

  const repeatLabel = t("player.repeat", { mode: t(REPEAT_MODES[player.repeat]) });

  const transportControls = (large = false) => (
    <div className={`transport ${large ? "transport-large" : ""}`}>
      <button
        type="button"
        className={`icon-button ${player.shuffle ? "is-active" : ""}`}
        onClick={player.toggleShuffle}
        aria-label={t("player.shuffle")}
        aria-pressed={player.shuffle}
        title={t("player.shuffle")}
      >
        <ShuffleIcon size={large ? 22 : 20} />
      </button>

      <button
        type="button"
        className="icon-button"
        onClick={player.previous}
        aria-label={t("player.previousTrack")}
        title={t("player.previousTrack")}
      >
        <PreviousIcon size={large ? 30 : 26} />
      </button>

      <PressButton
        className="play-button"
        onClick={player.toggle}
        aria-label={player.isPlaying ? t("action.pause") : t("action.play")}
      >
        {player.isPlaying ? (
          <PauseIcon size={large ? 34 : 26} />
        ) : (
          <PlayIcon size={large ? 34 : 26} />
        )}
      </PressButton>

      <button
        type="button"
        className="icon-button"
        onClick={player.next}
        aria-label={t("player.nextTrack")}
        title={t("player.nextTrack")}
      >
        <NextIcon size={large ? 30 : 26} />
      </button>

      <button
        type="button"
        className={`icon-button ${player.repeat !== "off" ? "is-active" : ""}`}
        onClick={player.cycleRepeat}
        aria-label={repeatLabel}
        title={repeatLabel}
      >
        {player.repeat === "one" ? (
          <RepeatOneIcon size={large ? 22 : 20} />
        ) : (
          <RepeatIcon size={large ? 22 : 20} />
        )}
      </button>
    </div>
  );

  return (
    <>
      <footer
        className="player"
        style={{
          ["--cover-tint" as string]: tint ?? "",
          ["--buffered" as string]: `${duration > 0 ? Math.min(100, (progress.buffered / duration) * 100) : 0}%`,
        }}
      >
        <Seekbar
          className="player-seek hide-mobile"
          value={progress.position}
          max={duration}
          onSeek={player.seek}
          ariaLabel={t("player.seek")}
          commitOnRelease
        />

        <div className="player-inner">
          <div className="player-track">
            <button
              type="button"
              className="player-cover-button"
              onClick={() => setExpanded(true)}
              aria-label={t("player.openFull")}
            >
              <Cover
                albumId={currentTrack.albumId}
                trackId={currentTrack.id}
                hasCover={currentTrack.hasCover}
                name={currentTrack.albumTitle ?? currentTrack.title}
                size="var(--player-cover)"
              />
            </button>

            <div className="player-meta">
              <span className="player-title">{currentTrack.title}</span>
              <ArtistLinks track={currentTrack} className="player-artist" />
            </div>

            <button
              type="button"
              className={`icon-button hide-mobile ${currentTrack.isFavorite ? "is-active" : ""}`}
              onClick={() => void toggleFavorite()}
              aria-label={
                currentTrack.isFavorite
                  ? t("tracks.removeFromFavorites")
                  : t("tracks.addToFavorites")
              }
              aria-pressed={currentTrack.isFavorite}
            >
              <HeartIcon size={20} filled={currentTrack.isFavorite} />
            </button>

            <div className="player-mobile-controls show-mobile">
              <button
                type="button"
                className="icon-button"
                onClick={player.toggle}
                aria-label={player.isPlaying ? t("action.pause") : t("action.play")}
              >
                {player.isPlaying ? <PauseIcon size={24} /> : <PlayIcon size={24} />}
              </button>

              <button
                type="button"
                className="icon-button"
                onClick={() => setExpanded(true)}
                aria-label={t("player.openFull")}
              >
                <ChevronUpIcon size={22} />
              </button>
            </div>
          </div>

          <div className="player-center hide-mobile">{transportControls()}</div>

          <div className="player-right hide-mobile">
            <span className="time time-pair">
              {formatDuration(progress.position)} / {formatDuration(duration)}
            </span>

            {settings.qualities.length > 1 && (
              <button
                type="button"
                className={`icon-button ${settings.dataSaver ? "is-active" : ""}`}
                onClick={() => settings.update({ dataSaver: !settings.dataSaver })}
                aria-label={t("player.dataSaver")}
                aria-pressed={settings.dataSaver}
                title={settings.dataSaver ? t("player.dataSaverOn") : t("player.dataSaverOff")}
              >
                <DataSaverIcon size={20} />
              </button>
            )}

            <button
              type="button"
              className={`icon-button ${queueOpen ? "is-active" : ""}`}
              onClick={() => setQueueOpen((open) => !open)}
              aria-label={t("queue.label")}
              aria-pressed={queueOpen}
              title={t("queue.title")}
            >
              <QueueIcon size={20} />
            </button>

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
              className="volume-bar"
            />
          </div>
        </div>

        <div className="player-mobile-progress show-mobile">
          <Seekbar
            value={progress.position}
            max={duration}
            onSeek={player.seek}
            ariaLabel={t("player.seek")}
            commitOnRelease
          />
        </div>
      </footer>

      <AnimatePresence>
        {queueOpen && <QueuePanel key="queue" onClose={() => setQueueOpen(false)} />}
      </AnimatePresence>

      <AnimatePresence>
        {expanded && (
          <FullScreenPlayer
            key="fullscreen"
            onClose={() => setExpanded(false)}
            transport={transportControls(true)}
            onToggleFavorite={() => void toggleFavorite()}
          />
        )}
      </AnimatePresence>
    </>
  );
}
