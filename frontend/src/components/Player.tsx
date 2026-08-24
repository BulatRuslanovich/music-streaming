// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { AnimatePresence } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { setNowPlaying } from "@/lib/documentTitle";
import { recordEvent } from "@/lib/events";
import { trackCoverUrl } from "@/lib/media";
import { formatArtists, formatDuration } from "@/lib/format";
import type { TranslationKey } from "@/lib/i18n";
import { useCoverAccent } from "@/lib/useCoverAccent";
import { useCoverColor, useCoverIsLight } from "@/lib/useCoverColor";
import { resolveShortcut, shortcutNeedsTrack } from "@/lib/shortcuts";
import { toggleRemainingTime, useRemainingTime } from "@/lib/useRemainingTime";
import { usePlayer, usePlayerProgress, type RepeatMode } from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { ArtistLinks } from "./ArtistLinks";
import { TrackCover } from "./Cover";
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

const VOLUME_STEP = 0.05;

const shellClass =
  "relative min-h-(--player-height) overflow-hidden rounded-xl border border-glass-border bg-glass px-5 py-2.5 backdrop-blur-2xl [grid-area:player] max-md:rounded-none max-md:border-x-0 max-md:border-b-0 max-md:px-2.5 max-md:pt-2 max-md:pb-1";

export function Player({ onOverlay }: { onOverlay: (overlay: "palette" | "shortcuts") => void }) {
  const player = usePlayer();
  const progress = usePlayerProgress();
  const settings = useSettings();
  const { notifyError } = useToast();
  const t = useT();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const showRemaining = useRemainingTime();
  const volumeRef = useRef<HTMLDivElement>(null);
  const { currentTrack } = player;

  const coverUrl = trackCoverUrl(currentTrack, "thumb");

  useCoverAccent(useCoverColor(coverUrl));

  const coverIsLight = useCoverIsLight(coverUrl);

  useEffect(() => {
    if (!currentTrack) {
      setNowPlaying(null);
      return;
    }

    setNowPlaying(
      `${player.isPlaying ? "▶" : "⏸"} ${currentTrack.title} — ${formatArtists(currentTrack)}`,
    );

    return () => setNowPlaying(null);
  }, [currentTrack, player.isPlaying]);

  useEffect(() => {
    const element = volumeRef.current;
    if (!element) return;

    const onWheel = (event: WheelEvent) => {
      if (event.deltaY === 0) return;

      event.preventDefault();
      const current = player.muted ? 0 : player.volume;
      player.setVolume(current + (event.deltaY < 0 ? VOLUME_STEP : -VOLUME_STEP));
    };

    element.addEventListener("wheel", onWheel, { passive: false });
    return () => element.removeEventListener("wheel", onWheel);
  }, [player]);

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

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const isTyping =
        target?.tagName === "INPUT" ||
        target?.tagName === "TEXTAREA" ||
        target?.isContentEditable === true;

      if (isTyping) return;

      const hit = resolveShortcut(event);
      if (!hit) return;

      const inOverlay =
        document.querySelector(
          "[data-state='open'][role='dialog'], [data-state='open'][role='menu']",
        ) !== null;
      if (inOverlay && hit.action !== "palette" && hit.action !== "help") return;

      if (!currentTrack && shortcutNeedsTrack(hit.action)) return;

      event.preventDefault();

      switch (hit.action) {
        case "playPause":
          player.toggle();
          break;
        case "seekBy":
          player.seekBy(hit.value ?? 0);
          break;
        case "seekPercent": {
          const total = progress.duration || currentTrack?.durationSeconds || 0;
          player.seek((total * (hit.value ?? 0)) / 100);
          break;
        }
        case "next":
          player.next();
          break;
        case "previous":
          player.previous();
          break;
        case "volumeBy":
          player.setVolume((player.muted ? 0 : player.volume) + (hit.value ?? 0));
          break;
        case "mute":
          player.toggleMute();
          break;
        case "favorite":
          void toggleFavorite();
          break;
        case "shuffle":
          player.toggleShuffle();
          break;
        case "repeat":
          player.cycleRepeat();
          break;
        case "queue":
          setQueueOpen((open) => !open);
          break;
        case "palette":
          onOverlay("palette");
          break;
        case "help":
          onOverlay("shortcuts");
          break;
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  });

  if (!currentTrack) {
    return (
      <footer className={cn(shellClass, "grid place-items-center")}>
        <p className="text-muted-foreground">{t("player.idle")}</p>
      </footer>
    );
  }

  const duration = progress.duration || currentTrack.durationSeconds;
  const repeatLabel = t("player.repeat", { mode: t(REPEAT_MODES[player.repeat]) });

  const transportControls = (layout: "bar" | "art" = "bar") => {
    const large = layout === "art";
    const activeOnArt =
      "bg-primary text-primary-foreground hover:bg-primary-hover hover:text-primary-foreground";

    const artGhost =
      large &&
      cn(
        "size-12",
        coverIsLight
          ? "text-black hover:bg-black/15 hover:text-black [filter:drop-shadow(0_1px_3px_rgb(255_255_255/0.55))]"
          : "text-white hover:bg-white/20 hover:text-white [filter:drop-shadow(0_1px_3px_rgb(0_0_0/0.5))]",
      );

    return (
      <div
        className={cn(
          "flex items-center",
          large ? "justify-center gap-4 max-[420px]:gap-1.5" : "gap-2",
        )}
      >
        <Button
          variant="ghost"
          size="icon"
          className={cn(
            large && "size-11",
            artGhost,
            player.shuffle && (large ? activeOnArt : "text-primary"),
          )}
          onClick={player.toggleShuffle}
          aria-label={t("player.shuffle")}
          aria-pressed={player.shuffle}
          title={t("player.shuffle")}
        >
          <ShuffleIcon size={large ? 22 : 20} />
        </Button>

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

        <Button
          variant="ghost"
          size="icon"
          className={cn(
            large && "size-11",
            artGhost,
            player.repeat !== "off" && (large ? activeOnArt : "text-primary"),
          )}
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
              <TrackCover track={currentTrack} size="var(--player-cover)" />
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
            <button
              type="button"
              onClick={toggleRemainingTime}
              aria-label={t("player.toggleRemaining")}
              title={t("player.toggleRemaining")}
              className="mr-1 rounded-sm text-xs whitespace-nowrap text-muted-foreground tabular-nums hover:text-foreground"
            >
              {formatDuration(progress.position)} /{" "}
              {showRemaining
                ? `-${formatDuration(Math.max(0, duration - progress.position))}`
                : formatDuration(duration)}
            </button>

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

            <div ref={volumeRef} className="pointer-events-auto flex items-center gap-1.5">
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
            transport={transportControls("art")}
            onToggleFavorite={() => void toggleFavorite()}
          />
        )}
      </AnimatePresence>
    </>
  );
}
