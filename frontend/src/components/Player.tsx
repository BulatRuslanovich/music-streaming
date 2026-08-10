"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { formatDuration } from "@/lib/format";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/contexts/ToastContext";
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

/**
 * The persistent player. It is rendered by the root layout, so it keeps its state and its audio
 * element across every navigation. On narrow screens the same state drives a full-screen sheet.
 */
export function Player() {
  const player = usePlayer();
  const { notifyError } = useToast();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const { currentTrack } = player;

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
      notifyError(error, "Could not update favourites.");
    }
  };

  if (!currentTrack) {
    return (
      <footer className="player player-idle">
        <p className="muted">Pick a track to start listening.</p>
      </footer>
    );
  }

  const duration = player.duration || currentTrack.durationSeconds;

  const transportControls = (large = false) => (
    <div className={`transport ${large ? "transport-large" : ""}`}>
      <button
        type="button"
        className={`icon-button ${player.shuffle ? "is-active" : ""}`}
        onClick={player.toggleShuffle}
        aria-label="Shuffle"
        aria-pressed={player.shuffle}
        title="Shuffle"
      >
        <ShuffleIcon size={large ? 22 : 18} />
      </button>

      <button
        type="button"
        className="icon-button"
        onClick={player.previous}
        aria-label="Previous track"
        title="Previous"
      >
        <PreviousIcon size={large ? 30 : 20} />
      </button>

      <button
        type="button"
        className="play-button"
        onClick={player.toggle}
        aria-label={player.isPlaying ? "Pause" : "Play"}
      >
        {player.isPlaying ? (
          <PauseIcon size={large ? 34 : 20} />
        ) : (
          <PlayIcon size={large ? 34 : 20} />
        )}
      </button>

      <button
        type="button"
        className="icon-button"
        onClick={player.next}
        aria-label="Next track"
        title="Next"
      >
        <NextIcon size={large ? 30 : 20} />
      </button>

      <button
        type="button"
        className={`icon-button ${player.repeat !== "off" ? "is-active" : ""}`}
        onClick={player.cycleRepeat}
        aria-label={`Repeat: ${player.repeat}`}
        title={`Repeat: ${player.repeat}`}
      >
        {player.repeat === "one" ? (
          <RepeatOneIcon size={large ? 22 : 18} />
        ) : (
          <RepeatIcon size={large ? 22 : 18} />
        )}
      </button>
    </div>
  );

  return (
    <>
      <footer className="player">
        {player.error && <p className="player-error">{player.error}</p>}

        <div className="player-inner">
          {/* Now playing ------------------------------------------------------------- */}
          <div className="player-track">
            <button
              type="button"
              className="player-cover-button"
              onClick={() => setExpanded(true)}
              aria-label="Open the full player"
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
              <Link href={`/artists/${currentTrack.artistId}`} className="player-artist">
                {currentTrack.artistName}
              </Link>
            </div>

            <button
              type="button"
              className={`icon-button hide-mobile ${currentTrack.isFavorite ? "is-active" : ""}`}
              onClick={() => void toggleFavorite()}
              aria-label={currentTrack.isFavorite ? "Remove from favourites" : "Add to favourites"}
              aria-pressed={currentTrack.isFavorite}
            >
              <HeartIcon size={18} filled={currentTrack.isFavorite} />
            </button>

            <button
              type="button"
              className="icon-button show-mobile"
              onClick={() => setExpanded(true)}
              aria-label="Open the full player"
            >
              <ChevronUpIcon size={20} />
            </button>
          </div>

          {/* Transport + progress ---------------------------------------------------- */}
          <div className="player-center hide-mobile">
            {transportControls()}

            <div className="player-progress">
              <span className="time">{formatDuration(player.position)}</span>
              <Seekbar
                value={player.position}
                max={duration}
                onSeek={player.seek}
                ariaLabel="Seek within the track"
              />
              <span className="time">{formatDuration(duration)}</span>
            </div>
          </div>

          {/* Volume + queue ---------------------------------------------------------- */}
          <div className="player-right hide-mobile">
            <button
              type="button"
              className={`icon-button ${queueOpen ? "is-active" : ""}`}
              onClick={() => setQueueOpen((open) => !open)}
              aria-label="Playback queue"
              aria-pressed={queueOpen}
              title="Queue"
            >
              <QueueIcon size={18} />
            </button>

            <button
              type="button"
              className="icon-button"
              onClick={player.toggleMute}
              aria-label={player.muted ? "Unmute" : "Mute"}
            >
              {player.muted || player.volume === 0 ? <MuteIcon size={18} /> : <VolumeIcon size={18} />}
            </button>

            <Seekbar
              value={player.muted ? 0 : player.volume}
              max={1}
              step={0.01}
              onSeek={player.setVolume}
              ariaLabel="Volume"
              className="volume-bar"
            />
          </div>
        </div>

        {/* A thin progress line is the only affordance that fits the mobile bar. */}
        <div className="player-mobile-progress show-mobile">
          <Seekbar
            value={player.position}
            max={duration}
            onSeek={player.seek}
            ariaLabel="Seek within the track"
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

/** Full-screen player for phones: large artwork and touch-sized controls. */
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
  const [showQueue, setShowQueue] = useState(false);
  const track = player.currentTrack;

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
    <div className="fullscreen-player" role="dialog" aria-modal="true" aria-label="Now playing">
      <header className="fullscreen-header">
        <button type="button" className="icon-button" onClick={onClose} aria-label="Close the full player">
          <CloseIcon size={22} />
        </button>
        <span className="muted">Now playing</span>
        <button
          type="button"
          className={`icon-button ${showQueue ? "is-active" : ""}`}
          onClick={() => setShowQueue((open) => !open)}
          aria-label="Playback queue"
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
            <Link href={`/artists/${track.artistId}`} onClick={onClose}>
              {track.artistName}
            </Link>
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
              ariaLabel="Seek within the track"
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
              aria-label={track.isFavorite ? "Remove from favourites" : "Add to favourites"}
            >
              <HeartIcon size={22} filled={track.isFavorite} />
            </button>

            <div className="fullscreen-volume">
              <button type="button" className="icon-button" onClick={player.toggleMute} aria-label="Mute">
                {player.muted || player.volume === 0 ? <MuteIcon size={18} /> : <VolumeIcon size={18} />}
              </button>
              <Seekbar
                value={player.muted ? 0 : player.volume}
                max={1}
                step={0.01}
                onSeek={player.setVolume}
                ariaLabel="Volume"
              />
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function QueuePanel({ onClose }: { onClose: () => void }) {
  return (
    <aside className="queue-panel" aria-label="Playback queue">
      <header>
        <h3>Queue</h3>
        <button type="button" className="icon-button" onClick={onClose} aria-label="Close the queue">
          <CloseIcon size={18} />
        </button>
      </header>
      <QueueList />
    </aside>
  );
}

function QueueList() {
  const player = usePlayer();

  if (player.queue.length === 0) {
    return <p className="empty-state">The queue is empty.</p>;
  }

  return (
    <>
      <div className="queue-actions">
        <span className="muted">
          {player.queue.length} track{player.queue.length === 1 ? "" : "s"}
        </span>
        <button type="button" className="text-button" onClick={player.clearQueue}>
          Clear
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
                <span className="queue-artist">{track.artistName}</span>
              </span>
              <span className="time">{formatDuration(track.durationSeconds)}</span>
            </button>

            <button
              type="button"
              className="icon-button"
              onClick={() => player.removeFromQueue(index)}
              aria-label={`Remove ${track.title} from the queue`}
            >
              <TrashIcon size={15} />
            </button>
          </li>
        ))}
      </ol>
    </>
  );
}
