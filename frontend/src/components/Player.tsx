// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/cn";
import { formatDuration } from "@/lib/format";
import { usePlaybackProgress } from "@/lib/playback/usePlaybackProgress";
import { useToggleFavorite } from "@/lib/useToggleFavorite";
import { usePlayerShortcuts } from "@/lib/playback/usePlayerShortcuts";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { ArtistLinks } from "./ArtistLinks";
import { Record } from "./Record";
import { Seekbar } from "./Seekbar";
import { PlayerTransport } from "./PlayerTransport";
import { PlayerVolume } from "./PlayerVolume";
import { DataSaverToggle } from "./DataSaverToggle";
import { FullScreenPlayer } from "./FullScreenPlayer";
import { QueuePanel } from "./QueuePanel";
import { Button } from "./ui/button";
import {
  ChevronUpIcon,
  HeartIcon,
  ListVideoIcon,
  Maximize2Icon,
  PauseIcon,
  PlayIcon,
  SkipForwardIcon,
} from "lucide-react";

const VOLUME_STEP = 0.05;

const shellClass =
  "relative min-h-(--player-height) border-t border-border bg-card px-4 py-2.5 [grid-area:player] max-md:px-2.5 max-md:pt-2 max-md:pb-1";

/**
 * Полоса перемотки со временем по краям — единственное, чему нужен контекст прогресса, и
 * поэтому единственное, что перерисовывается по его тику (четыре раза в секунду).
 * `fallbackDuration` покрывает окно до `loadedmetadata`, когда декодированной длительности
 * ещё нет, а в метаданных трека она уже есть.
 *
 * Один компонент на оба места, а не два почти одинаковых. Различие ровно одно: на десктопе
 * есть подпись под курсором, на тач-экране она бессмысленна.
 */
function ProgressRow({
  fallbackDuration,
  tooltip = false,
  className,
}: {
  fallbackDuration: number;
  tooltip?: boolean;
  className?: string;
}) {
  const progress = usePlaybackProgress(fallbackDuration);

  const clock = "w-10 shrink-0 text-2xs text-faint tabular-nums";

  return (
    <div className={cn("flex w-full items-center gap-2", className)}>
      <span className={cn(clock, "text-right")}>{formatDuration(progress.position)}</span>

      <Seekbar
        className="min-w-0 flex-1"
        variant="player"
        value={progress.position}
        max={progress.total}
        onSeek={progress.seek}
        ariaLabel={progress.seekLabel}
        style={{ ["--buffered" as string]: `${progress.bufferedPercent}%` }}
        tooltip={tooltip ? formatDuration : undefined}
        commitOnRelease
      />

      <button
        type="button"
        onClick={progress.toggleRemainingTime}
        aria-label={progress.toggleRemainingLabel}
        title={progress.toggleRemainingLabel}
        className={cn(clock, "rounded-sm text-left hover:text-foreground")}
      >
        {progress.endLabel}
      </button>
    </div>
  );
}

export function Player() {
  // INFO: прогресс сюда сознательно не подписан — он тикает 4 раза в секунду и утащил бы
  // за собой очередь и полноэкранный плеер. Его читает только ProgressRow.
  const state = usePlayerState();
  const actions = usePlayerActions();
  const settings = useSettings();
  const t = useT();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const volumeRef = useRef<HTMLDivElement>(null);
  const { currentTrack } = state;

  usePlayerShortcuts(() => setQueueOpen((open) => !open));

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

  if (!currentTrack) {
    return (
      <footer className={cn(shellClass, "grid place-items-center")}>
        <p className="text-sm text-muted-foreground">{t("player.idle")}</p>
      </footer>
    );
  }

  const favoriteLabel = currentTrack.isFavorite
    ? t("tracks.removeFromFavorites")
    : t("tracks.addToFavorites");

  return (
    <>
      <footer className={shellClass}>
        {/* `h-auto` на телефоне обязателен: с `h-full` эта строка забирала всю высоту футера,
            и полоса со временем под ней уходила под обрез. */}
        <div className="grid h-full grid-cols-[minmax(0,1fr)_minmax(0,36rem)_minmax(0,1fr)] items-center gap-6 max-md:h-auto max-md:grid-cols-1 max-md:gap-0">
          <div className="flex min-w-0 items-center gap-3 max-md:gap-2.5">
            {/* Пока трек играет, из-за конверта выглядывает край диска — запас справа под него. */}
            <button
              type="button"
              onClick={() => setExpanded(true)}
              aria-label={t("player.openFull")}
              className="mr-2.5 shrink-0 leading-none"
            >
              <Record
                track={currentTrack}
                out={state.isPlaying}
                spinning={state.isPlaying}
                className="w-(--player-cover) [--record-out:20%]"
              />
            </button>

            <div className="flex min-w-0 flex-col">
              {/* Название ведёт на альбом: раньше клик по нему проваливался на полосу
                  перемотки и сбивал позицию в треке. */}
              {currentTrack.albumId ? (
                <Link
                  href={`/albums/${currentTrack.albumId}`}
                  className="truncate font-medium hover:underline"
                >
                  {currentTrack.title}
                </Link>
              ) : (
                <span className="truncate font-medium">{currentTrack.title}</span>
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
              aria-label={favoriteLabel}
              aria-pressed={currentTrack.isFavorite}
            >
              <HeartIcon className={currentTrack.isFavorite ? "fill-current" : undefined} />
            </Button>

            <div className="ml-auto flex items-center gap-0.5 md:hidden">
              <Button
                variant="ghost"
                size="icon"
                onClick={actions.toggle}
                aria-label={state.isPlaying ? t("action.pause") : t("action.play")}
              >
                {state.isPlaying ? <PauseIcon size={24} /> : <PlayIcon size={24} />}
              </Button>

              <Button
                variant="ghost"
                size="icon"
                onClick={actions.next}
                aria-label={t("player.nextTrack")}
              >
                <SkipForwardIcon size={24} />
              </Button>

              <Button
                variant="ghost"
                size="icon"
                onClick={() => setExpanded(true)}
                aria-label={t("player.openFull")}
              >
                <ChevronUpIcon />
              </Button>
            </div>
          </div>

          <div className="flex min-w-0 flex-col items-center gap-1 max-md:hidden">
            <PlayerTransport />
            <ProgressRow tooltip fallbackDuration={currentTrack.durationSeconds} />
          </div>

          <div className="flex min-w-0 items-center justify-end gap-1.5 max-md:hidden">
            <DataSaverToggle
              // Тише остальных в покое: это переключатель на весь сеанс, а не то, чем
              // пользуются в каждом треке. Включённым он говорит акцентом в полный голос.
              className={cn(
                "text-faint hover:text-foreground max-xl:hidden",
                settings.dataSaver && "hover:text-primary",
              )}
              withTitle
            />

            <Button
              variant="ghost"
              size="icon"
              className={cn(queueOpen && "text-primary")}
              onClick={() => setQueueOpen((open) => !open)}
              aria-label={t("queue.label")}
              aria-pressed={queueOpen}
              title={
                state.nextTrack
                  ? t("player.upNextNamed", { title: state.nextTrack.title })
                  : t("queue.title")
              }
            >
              <ListVideoIcon />
            </Button>

            <div ref={volumeRef} className="flex items-center gap-1.5">
              <PlayerVolume seekbarClassName="max-w-[7.5rem]" />
            </div>

            <Button
              variant="ghost"
              size="icon"
              onClick={() => setExpanded(true)}
              aria-label={t("player.openFull")}
              title={t("player.openFull")}
            >
              <Maximize2Icon size={18} />
            </Button>
          </div>
        </div>

        <div className="md:hidden">
          <ProgressRow fallbackDuration={currentTrack.durationSeconds} />
        </div>
      </footer>

      {queueOpen && <QueuePanel onClose={() => setQueueOpen(false)} />}

      {expanded && (
        <FullScreenPlayer onClose={() => setExpanded(false)} onToggleFavorite={likeCurrent} />
      )}
    </>
  );
}
