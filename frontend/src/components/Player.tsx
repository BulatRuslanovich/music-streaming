"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api, trackCoverUrl } from "@/lib/api";
import { formatArtists, formatDuration } from "@/lib/format";
import type { TranslationKey } from "@/lib/i18n";
import { useCoverColor } from "@/lib/useCoverColor";
import { usePlayer, type RepeatMode } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { ArtistLinks } from "./ArtistLinks";
import { Cover } from "./Cover";
import { Seekbar } from "./Seekbar";
import {
  ChevronUpIcon,
  CloseIcon,
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
  TrashIcon,
  VolumeIcon,
} from "./Icons";


const REPEAT_MODES: Record<RepeatMode, TranslationKey> = {
  off: "player.repeatOff",
  one: "player.repeatOne",
  all: "player.repeatAll",
};

export function Player() {
  const player = usePlayer();
  const { notifyError } = useToast();
  const t = useT();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const { currentTrack } = player;

  // The bar takes on the colour of the artwork it is showing.
  const tint = useCoverColor(trackCoverUrl(currentTrack));

  // Space toggles playback, arrows seek — but never while the user is typing.
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
            player.seek(player.position + 10);
          }
          break;
        case "ArrowLeft":
          if (event.shiftKey) {
            event.preventDefault();
            player.previous();
          } else if (event.altKey) {
            event.preventDefault();
            player.seek(player.position - 10);
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

  const duration = player.duration || currentTrack.durationSeconds;

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

      <button
        type="button"
        className="play-button"
        onClick={player.toggle}
        aria-label={player.isPlaying ? t("action.pause") : t("action.play")}
      >
        {player.isPlaying ? (
          <PauseIcon size={large ? 34 : 26} />
        ) : (
          <PlayIcon size={large ? 34 : 26} />
        )}
      </button>

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
          ["--buffered" as string]: `${duration > 0 ? Math.min(100, (player.buffered / duration) * 100) : 0}%`,
        }}
      >
        {/*
          The seek control covers the whole bar and paints itself as its background: the played
          part of the track is a wash across the entire panel rather than a separate thin line.
          It sits under the controls, which let clicks through everywhere they have no button, so
          the bar can be scrubbed almost anywhere along its length.
        */}
        <Seekbar
          className="player-seek hide-mobile"
          value={player.position}
          max={duration}
          onSeek={player.seek}
          ariaLabel={t("player.seek")}
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
                size={56}
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

            <button
              type="button"
              className="icon-button show-mobile"
              onClick={() => setExpanded(true)}
              aria-label={t("player.openFull")}
            >
              <ChevronUpIcon size={22} />
            </button>
          </div>

          <div className="player-center hide-mobile">{transportControls()}</div>

          <div className="player-right hide-mobile">
            <span className="time time-pair">
              {formatDuration(player.position)} / {formatDuration(duration)}
            </span>

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
              {player.muted || player.volume === 0 ? <MuteIcon size={20} /> : <VolumeIcon size={20} />}
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
            value={player.position}
            max={duration}
            onSeek={player.seek}
            ariaLabel={t("player.seek")}
          />
        </div>
      </footer>

      {queueOpen && <QueuePanel onClose={() => setQueueOpen(false)} />}

      {expanded && (
        <FullScreenPlayer
          onClose={() => setExpanded(false)}
          transport={transportControls(true)}
          onToggleFavorite={() => void toggleFavorite()}
        />
      )}
    </>
  );
}

function FullScreenPlayer({
  onClose,
  transport,
  onToggleFavorite,
}: {
  onClose: () => void;
  transport: React.ReactNode;
  onToggleFavorite: () => void;
}) {
  const player = usePlayer();
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

  const duration = player.duration || track.durationSeconds;

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
        <button
          type="button"
          className={`icon-button ${showQueue ? "is-active" : ""}`}
          onClick={() => setShowQueue((open) => !open)}
          aria-label={t("queue.label")}
        >
          <QueueIcon size={20} />
        </button>
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
              value={player.position}
              max={duration}
              onSeek={player.seek}
              ariaLabel={t("player.seek")}
            />
            <div className="fullscreen-times">
              <span>{formatDuration(player.position)}</span>
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

function QueuePanel({ onClose }: { onClose: () => void }) {
  const t = useT();

  return (
    <aside className="queue-panel" aria-label={t("queue.label")}>
      <header>
        <h3>{t("queue.title")}</h3>
        <button type="button" className="icon-button" onClick={onClose} aria-label={t("queue.close")}>
          <CloseIcon size={18} />
        </button>
      </header>
      <QueueList />
    </aside>
  );
}

function QueueList() {
  const player = usePlayer();
  const t = useT();

  if (player.queue.length === 0) {
    return <p className="empty-state">{t("queue.empty")}</p>;
  }

  return (
    <>
      <div className="queue-actions">
        <span className="muted">{t("count.tracks", { count: player.queue.length })}</span>
        <button type="button" className="text-button" onClick={player.clearQueue}>
          {t("action.clear")}
        </button>
      </div>

      <ol className="queue-list">
        {player.queue.map((track, index) => (
          <li
            key={`${track.id}-${index}`}
            className={index === player.currentIndex ? "is-current" : ""}
          >
            <button
              type="button"
              className="queue-item"
              onClick={() => player.jumpTo(index)}
              aria-current={index === player.currentIndex}
            >
              <Cover
                albumId={track.albumId}
                trackId={track.id}
                hasCover={track.hasCover}
                name={track.albumTitle ?? track.title}
                size={36}
              />
              <span className="queue-meta">
                <span className="queue-title">{track.title}</span>
                {/* Plain text, not links: the whole queue row is a button. */}
                <span className="queue-artist">{formatArtists(track)}</span>
              </span>
              <span className="time">{formatDuration(track.durationSeconds)}</span>
            </button>

            <button
              type="button"
              className="icon-button"
              onClick={() => player.removeFromQueue(index)}
              aria-label={t("queue.removeNamed", { title: track.title })}
            >
              <TrashIcon size={15} />
            </button>
          </li>
        ))}
      </ol>
    </>
  );
}
