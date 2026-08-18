"use client";

import { AnimatePresence } from "motion/react";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { recordEvent } from "@/lib/events";
import { trackCoverUrl } from "@/lib/media";
import { formatDuration } from "@/lib/format";
import type { TranslationKey } from "@/lib/i18n";
import { useCoverAccent } from "@/lib/useCoverAccent";
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
import { Button, PressButton } from "./ui/button";
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

const shellClass =
  "relative min-h-(--player-height) overflow-hidden rounded-xl border border-glass-border bg-glass px-5 py-2.5 backdrop-blur-2xl [grid-area:player] max-md:rounded-none max-md:border-x-0 max-md:border-b-0 max-md:px-2.5 max-md:pt-2 max-md:pb-1";

export function Player() {
  const player = usePlayer();
  const progress = usePlayerProgress();
  const settings = useSettings();
  const { notifyError } = useToast();
  const t = useT();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const { currentTrack } = player;

  useCoverAccent(useCoverColor(trackCoverUrl(currentTrack, "thumb")));

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
      <footer className={cn(shellClass, "grid place-items-center")}>
        <p className="text-muted-foreground">{t("player.idle")}</p>
      </footer>
    );
  }

  const duration = progress.duration || currentTrack.durationSeconds;
  const repeatLabel = t("player.repeat", { mode: t(REPEAT_MODES[player.repeat]) });

  const transportControls = (layout: "bar" | "full" | "art" = "bar") => {
    const large = layout !== "bar";
    const onArt = layout === "art";
    const artGhost = onArt && "size-12 text-white hover:bg-white/20 hover:text-white";

    return (
      <div className={cn("flex items-center", large ? "justify-center gap-4" : "gap-2")}>
        {!onArt && (
          <Button
            variant="ghost"
            size="icon"
            className={cn(large && "size-11", player.shuffle && "text-primary")}
            onClick={player.toggleShuffle}
            aria-label={t("player.shuffle")}
            aria-pressed={player.shuffle}
            title={t("player.shuffle")}
          >
            <ShuffleIcon size={large ? 22 : 20} />
          </Button>
        )}

        <Button
          variant="ghost"
          size="icon"
          className={cn(large ? "size-11" : "size-10", artGhost)}
          onClick={player.previous}
          aria-label={t("player.previousTrack")}
          title={t("player.previousTrack")}
        >
          <PreviousIcon size={large ? 30 : 26} />
        </Button>

        <PressButton
          variant="play"
          size={large ? "play-lg" : "play"}
          onClick={player.toggle}
          aria-label={player.isPlaying ? t("action.pause") : t("action.play")}
        >
          {player.isPlaying ? (
            <PauseIcon size={large ? 34 : 26} />
          ) : (
            <PlayIcon size={large ? 34 : 26} />
          )}
        </PressButton>

        <Button
          variant="ghost"
          size="icon"
          className={cn(large ? "size-11" : "size-10", artGhost)}
          onClick={player.next}
          aria-label={t("player.nextTrack")}
          title={t("player.nextTrack")}
        >
          <NextIcon size={large ? 30 : 26} />
        </Button>

        {!onArt && (
          <Button
            variant="ghost"
            size="icon"
            className={cn(large && "size-11", player.repeat !== "off" && "text-primary")}
            onClick={player.cycleRepeat}
            aria-label={repeatLabel}
            title={repeatLabel}
          >
            {player.repeat === "one" ? (
              <RepeatOneIcon size={large ? 22 : 20} />
            ) : (
              <RepeatIcon size={large ? 22 : 20} />
            )}
          </Button>
        )}
      </div>
    );
  };

  return (
    <>
      <footer
        className={shellClass}
        style={{
          ["--buffered" as string]: `${duration > 0 ? Math.min(100, (progress.buffered / duration) * 100) : 0}%`,
        }}
      >
        <span
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 bg-[color-mix(in_srgb,var(--cover-tint)_20%,transparent)]"
        />

        <Seekbar
          className="player-seek max-md:hidden"
          value={progress.position}
          max={duration}
          onSeek={player.seek}
          ariaLabel={t("player.seek")}
          commitOnRelease
        />

        <div className="pointer-events-none relative z-1 grid h-full grid-cols-[minmax(0,1fr)_minmax(0,2fr)_minmax(0,1fr)] items-center gap-5 max-md:grid-cols-1 max-md:gap-0 [&_a]:pointer-events-auto [&_button]:pointer-events-auto [&_input]:pointer-events-auto">
          <div className="flex min-w-0 items-center gap-3 max-md:gap-2.5">
            <button
              type="button"
              onClick={() => setExpanded(true)}
              aria-label={t("player.openFull")}
              className="rounded-md leading-none shadow-art"
            >
              <Cover
                albumId={currentTrack.albumId}
                trackId={currentTrack.id}
                hasCover={currentTrack.hasCover}
                name={currentTrack.albumTitle ?? currentTrack.title}
                size="var(--player-cover)"
              />
            </button>

            <div className="flex min-w-0 flex-col">
              <span className="truncate font-semibold">{currentTrack.title}</span>
              <ArtistLinks
                track={currentTrack}
                className="truncate text-sm text-muted-foreground"
              />
            </div>

            <Button
              variant="ghost"
              size="icon"
              className={cn("max-md:hidden", currentTrack.isFavorite && "text-primary")}
              onClick={() => void toggleFavorite()}
              aria-label={
                currentTrack.isFavorite
                  ? t("tracks.removeFromFavorites")
                  : t("tracks.addToFavorites")
              }
              aria-pressed={currentTrack.isFavorite}
            >
              <HeartIcon size={20} filled={currentTrack.isFavorite} />
            </Button>

            <div className="md:hidden ml-auto flex items-center gap-0.5">
              <Button
                variant="ghost"
                size="icon"
                onClick={player.toggle}
                aria-label={player.isPlaying ? t("action.pause") : t("action.play")}
              >
                {player.isPlaying ? <PauseIcon size={24} /> : <PlayIcon size={24} />}
              </Button>

              <Button
                variant="ghost"
                size="icon"
                onClick={() => setExpanded(true)}
                aria-label={t("player.openFull")}
              >
                <ChevronUpIcon size={22} />
              </Button>
            </div>
          </div>

          <div className="max-md:hidden flex min-w-0 flex-col items-center gap-1">
            {transportControls()}
          </div>

          <div className="max-md:hidden flex items-center justify-end gap-1.5">
            <span className="mr-1 text-xs whitespace-nowrap text-muted-foreground tabular-nums">
              {formatDuration(progress.position)} / {formatDuration(duration)}
            </span>

            {settings.qualities.length > 1 && (
              <Button
                variant="ghost"
                size="icon"
                className={cn(settings.dataSaver && "text-primary")}
                onClick={() => settings.update({ dataSaver: !settings.dataSaver })}
                aria-label={t("player.dataSaver")}
                aria-pressed={settings.dataSaver}
                title={settings.dataSaver ? t("player.dataSaverOn") : t("player.dataSaverOff")}
              >
                <DataSaverIcon size={20} />
              </Button>
            )}

            <Button
              variant="ghost"
              size="icon"
              className={cn(queueOpen && "text-primary")}
              onClick={() => setQueueOpen((open) => !open)}
              aria-label={t("queue.label")}
              aria-pressed={queueOpen}
              title={t("queue.title")}
            >
              <QueueIcon size={20} />
            </Button>

            <Button
              variant="ghost"
              size="icon"
              onClick={player.toggleMute}
              aria-label={player.muted ? t("player.unmute") : t("player.mute")}
            >
              {player.muted || player.volume === 0 ? (
                <MuteIcon size={20} />
              ) : (
                <VolumeIcon size={20} />
              )}
            </Button>

            <Seekbar
              value={player.muted ? 0 : player.volume}
              max={1}
              step={0.01}
              onSeek={player.setVolume}
              ariaLabel={t("player.volume")}
              className="max-w-[7.5rem]"
            />
          </div>
        </div>

        <div className="md:hidden relative z-1">
          <Seekbar
            className="h-3.5"
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
            transport={transportControls("full")}
            artTransport={transportControls("art")}
            onToggleFavorite={() => void toggleFavorite()}
          />
        )}
      </AnimatePresence>
    </>
  );
}
