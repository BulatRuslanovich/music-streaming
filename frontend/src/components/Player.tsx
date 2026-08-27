// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { AnimatePresence } from "motion/react";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/cn";
import { trackCoverUrl } from "@/lib/media";
import { formatDuration } from "@/lib/format";
import type { TranslationKey } from "@/lib/i18n";
import { useCoverAccent } from "@/lib/useCoverAccent";
import { useCoverPalette } from "@/lib/useCoverColor";
import { resolveShortcut, shortcutNeedsTrack } from "@/lib/shortcuts";
import { toggleRemainingTime, useRemainingTime } from "@/lib/useRemainingTime";
import { useToggleFavorite } from "@/lib/useToggleFavorite";
import { useWindowKeyDown } from "@/lib/useWindowKeyDown";
import {
  usePlayerActions,
  usePlayerProgress,
  usePlayerState,
  type RepeatMode,
} from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
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
  "relative min-h-(--player-height) overflow-hidden bg-canvas px-5 py-2.5 [grid-area:player] max-md:px-2.5 max-md:pt-2 max-md:pb-1";

/**
 * Полоса и часы — единственные, кому нужен контекст прогресса, и поэтому единственные,
 * кто перерисовывается по его тику. `fallbackDuration` покрывает окно до `loadedmetadata`,
 * когда декодированной длительности ещё нет, а в метаданных трека она уже есть.
 */
function PlayerSeek({
  className,
  fallbackDuration,
}: {
  className?: string;
  fallbackDuration: number;
}) {
  const { position, duration, buffered } = usePlayerProgress();
  const { seek } = usePlayerActions();
  const t = useT();

  const total = duration || fallbackDuration;
  const bufferedPercent = total > 0 ? Math.min(100, (buffered / total) * 100) : 0;

  return (
    <Seekbar
      className={className}
      value={position}
      max={total}
      onSeek={seek}
      ariaLabel={t("player.seek")}
      style={{ ["--buffered" as string]: `${bufferedPercent}%` }}
      commitOnRelease
    />
  );
}

function PlayerTime({ fallbackDuration }: { fallbackDuration: number }) {
  const { position, duration } = usePlayerProgress();
  const showRemaining = useRemainingTime();
  const t = useT();

  const total = duration || fallbackDuration;

  return (
    <button
      type="button"
      onClick={toggleRemainingTime}
      aria-label={t("player.toggleRemaining")}
      title={t("player.toggleRemaining")}
      className="mr-1 rounded-sm text-xs whitespace-nowrap text-muted-foreground tabular-nums hover:text-foreground"
    >
      {formatDuration(position)} /{" "}
      {showRemaining ? `-${formatDuration(Math.max(0, total - position))}` : formatDuration(total)}
    </button>
  );
}

export function Player({ onOverlay }: { onOverlay: (overlay: "palette" | "shortcuts") => void }) {
  // INFO: прогресс сюда сознательно не подписан — он тикает 4 раза в секунду и утащил бы
  // за собой очередь и полноэкранный плеер. Его читают только PlayerSeek и PlayerTime.
  const state = usePlayerState();
  const actions = usePlayerActions();
  const player = { ...state, ...actions };
  const settings = useSettings();
  const t = useT();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const volumeRef = useRef<HTMLDivElement>(null);
  const { currentTrack } = state;

  const coverUrl = trackCoverUrl(currentTrack, "thumb");

  const palette = useCoverPalette(coverUrl);

  useCoverAccent(palette.tint, palette.tintAlt);

  useEffect(() => {
    const element = volumeRef.current;
    if (!element) return;

    const onWheel = (event: WheelEvent) => {
      if (event.deltaY === 0) return;

      event.preventDefault();
      const current = state.muted ? 0 : state.volume;
      actions.setVolume(current + (event.deltaY < 0 ? VOLUME_STEP : -VOLUME_STEP));
    };

    element.addEventListener("wheel", onWheel, { passive: false });
    return () => element.removeEventListener("wheel", onWheel);
  }, [state.muted, state.volume, actions]);

  const toggleFavorite = useToggleFavorite();
  const likeCurrent = () => {
    if (currentTrack) void toggleFavorite(currentTrack);
  };

  useWindowKeyDown((event) => {
    const target = event.target as HTMLElement | null;
    const isTyping =
      target?.tagName === "INPUT" ||
      target?.tagName === "TEXTAREA" ||
      target?.isContentEditable === true;

    if (isTyping) return;

    const hit = resolveShortcut(event);
    if (!hit) return;

    // Полноэкранный плеер — тоже диалог, но это сам плеер, и клавиши в нём должны работать.
    const inOverlay =
      document.querySelector(
        "[data-state='open'][role='dialog']:not([data-player-fullscreen]), [data-state='open'][role='menu']",
      ) !== null;
    if (inOverlay && hit.action !== "palette" && hit.action !== "help") return;

    if (!currentTrack && shortcutNeedsTrack(hit.action)) return;

    event.preventDefault();

    switch (hit.action) {
      case "playPause":
        actions.toggle();
        break;
      case "seekBy":
        actions.seekBy(hit.value ?? 0);
        break;
      case "seekPercent": {
        // Длину берём функцией, а не из контекста прогресса: подписка на него стоила бы
        // плееру перерисовки на каждый тик ради одной цифры, нужной раз в нажатие.
        const total = actions.getDuration() || currentTrack?.durationSeconds || 0;
        actions.seek((total * (hit.value ?? 0)) / 100);
        break;
      }
      case "next":
        actions.next();
        break;
      case "previous":
        actions.previous();
        break;
      case "volumeBy":
        actions.setVolume((state.muted ? 0 : state.volume) + (hit.value ?? 0));
        break;
      case "mute":
        actions.toggleMute();
        break;
      case "favorite":
        likeCurrent();
        break;
      case "shuffle":
        actions.toggleShuffle();
        break;
      case "repeat":
        actions.cycleRepeat();
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
  });

  if (!currentTrack) {
    return (
      <footer className={cn(shellClass, "grid place-items-center")}>
        <p className="text-muted-foreground">{t("player.idle")}</p>
      </footer>
    );
  }

  const repeatLabel = t("player.repeat", { mode: t(REPEAT_MODES[player.repeat]) });

  /**
   * Транспорт в двух размерах. Раньше вариант `art` рисовался поверх обложки и поэтому
   * подбирал чёрный или белый по светлоте самой обложки; теперь он стоит в обычном потоке
   * полноэкранного плеера и живёт на тех же токенах, что и всё остальное.
   */
  const transportControls = (layout: "bar" | "full" = "bar") => {
    const large = layout === "full";

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
          className={cn(large && "size-11", player.shuffle && "text-primary")}
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
          className={large ? "size-11" : "size-10"}
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
          className={large ? "size-11" : "size-10"}
          onClick={player.next}
          aria-label={t("player.nextTrack")}
          title={t("player.nextTrack")}
        >
          <NextIcon size={large ? 30 : 26} />
        </Button>

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
      </div>
    );
  };

  return (
    <>
      <footer className={shellClass}>
        <PlayerSeek
          className="player-seek max-md:hidden"
          fallbackDuration={currentTrack.durationSeconds}
        />

        <div className="relative z-1 grid h-full grid-cols-[minmax(0,1fr)_minmax(0,2fr)_minmax(0,1fr)] items-center gap-5 max-md:grid-cols-1 max-md:gap-0">
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
              {/* Название ведёт на альбом: раньше клик по нему проваливался на полосу
                  перемотки, растянутую на весь футер, и сбивал позицию в треке. */}
              {currentTrack.albumId ? (
                <Link
                  href={`/albums/${currentTrack.albumId}`}
                  className="truncate font-semibold hover:underline"
                >
                  {currentTrack.title}
                </Link>
              ) : (
                <span className="truncate font-semibold">{currentTrack.title}</span>
              )}
              <ArtistLinks
                track={currentTrack}
                className="truncate text-sm text-muted-foreground"
              />
            </div>

            <Button
              variant="ghost"
              size="icon"
              className={cn("max-md:hidden", currentTrack.isFavorite && "text-primary")}
              onClick={likeCurrent}
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

              {/* Пропуск — вторая по частоте операция после паузы, а до этого он был
                  доступен только из полноэкранного плеера или системных медиа-кнопок. */}
              <Button
                variant="ghost"
                size="icon"
                onClick={player.next}
                aria-label={t("player.nextTrack")}
              >
                <NextIcon size={24} />
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
            <PlayerTime fallbackDuration={currentTrack.durationSeconds} />

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

            <div ref={volumeRef} className="flex items-center gap-1.5">
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
          <PlayerSeek className="h-3.5" fallbackDuration={currentTrack.durationSeconds} />
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
            onToggleFavorite={likeCurrent}
          />
        )}
      </AnimatePresence>
    </>
  );
}
