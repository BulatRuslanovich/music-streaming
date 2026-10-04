// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/cn";
import { formatDuration } from "@/lib/format";
import { SEEK_STEP } from "@/lib/shortcuts";
import { usePlaybackProgress } from "@/lib/playback/usePlaybackProgress";
import { useToggleFavorite } from "@/lib/useToggleFavorite";
import { usePlayerShortcuts } from "@/lib/playback/usePlayerShortcuts";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import { useSettings } from "@/lib/useSettings";
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

const SWIPE_OPEN_PX = 40;

const shellClass =
  "relative min-h-(--player-height) border-t border-border bg-card px-4 py-2.5 [grid-area:player] max-md:border-t-0 max-md:px-2.5 max-md:py-2";

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
        valueText={progress.valueText}
        keyStep={SEEK_STEP}
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

function ProgressLine({ fallbackDuration }: { fallbackDuration: number }) {
  const { position, total } = usePlaybackProgress(fallbackDuration);

  return (
    <span aria-hidden="true" className="absolute inset-x-0 top-0 h-0.5 bg-border md:hidden">
      <span
        className="block h-full bg-foreground"
        style={{ width: `${total > 0 ? Math.min(100, (position / total) * 100) : 0}%` }}
      />
    </span>
  );
}

export function Player() {
  const state = usePlayerState();
  const actions = usePlayerActions();
  const settings = useSettings();
  const t = useT();

  const [expanded, setExpanded] = useState(false);
  const [queueOpen, setQueueOpen] = useState(false);
  const volumeRef = useRef<HTMLDivElement>(null);
  const swipeFrom = useRef<number | null>(null);
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

  if (!currentTrack) return null;

  const favoriteLabel = currentTrack.isFavorite
    ? t("tracks.removeFromFavorites")
    : t("tracks.addToFavorites");

  return (
    <>
      <footer
        className={shellClass}
        onTouchStart={(event) => (swipeFrom.current = event.touches[0].clientY)}
        onTouchEnd={(event) => {
          const from = swipeFrom.current;
          swipeFrom.current = null;
          if (from !== null && from - event.changedTouches[0].clientY > SWIPE_OPEN_PX)
            setExpanded(true);
        }}
      >
        <div className="grid h-full grid-cols-[minmax(0,1fr)_minmax(0,36rem)_minmax(0,1fr)] items-center gap-6 max-md:h-auto max-md:grid-cols-1 max-md:gap-0">
          <div className="flex min-w-0 items-center gap-3 max-md:gap-2.5">
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

        <ProgressLine fallbackDuration={currentTrack.durationSeconds} />
      </footer>

      {queueOpen && <QueuePanel onClose={() => setQueueOpen(false)} />}

      {expanded && (
        <FullScreenPlayer onClose={() => setExpanded(false)} onToggleFavorite={likeCurrent} />
      )}
    </>
  );
}
