// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import type { TranslationKey } from "@/lib/i18n";
import { usePlayerActions, usePlayerState, type RepeatMode } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Button, PressButton } from "./ui/button";
import {
  NextIcon,
  PauseIcon,
  PlayIcon,
  PreviousIcon,
  RepeatIcon,
  RepeatOneIcon,
  ShuffleIcon,
} from "./Icons";

const REPEAT_MODES: Record<RepeatMode, TranslationKey> = {
  off: "player.repeatOff",
  one: "player.repeatOne",
  all: "player.repeatAll",
};

/**
 * Кнопки перемотки в двух размерах. Раньше это была функция внутри `Player`, а готовый
 * JSX уезжал пропом `transport` в полноэкранный плеер — из-за чего тот не мог существовать
 * без панели, разметка пересобиралась на каждый рендер `Player`, а чтобы понять, что за
 * кнопки на полноэкранном экране, приходилось читать другой файл.
 *
 * Состояние компонент читает сам; прогресс он не трогает, поэтому тик позиции его не
 * перерисовывает.
 */
export function PlayerTransport({ size = "bar" }: { size?: "bar" | "full" }) {
  const { isPlaying, shuffle, repeat } = usePlayerState();
  const { toggle, next, previous, toggleShuffle, cycleRepeat } = usePlayerActions();
  const t = useT();

  const large = size === "full";
  const repeatLabel = t("player.repeat", { mode: t(REPEAT_MODES[repeat]) });

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
        className={cn(large && "size-11", shuffle && "text-primary")}
        onClick={toggleShuffle}
        aria-label={t("player.shuffle")}
        aria-pressed={shuffle}
        title={t("player.shuffle")}
      >
        <ShuffleIcon size={large ? 24 : 20} />
      </Button>

      <Button
        variant="ghost"
        size="icon"
        className={large ? "size-11" : "size-10"}
        onClick={previous}
        aria-label={t("player.previousTrack")}
        title={t("player.previousTrack")}
      >
        <PreviousIcon size={large ? 34 : 28} />
      </Button>

      <PressButton
        variant="play"
        size={large ? "play-lg" : "play"}
        onClick={toggle}
        aria-label={isPlaying ? t("action.pause") : t("action.play")}
      >
        {isPlaying ? <PauseIcon size={large ? 34 : 28} /> : <PlayIcon size={large ? 34 : 28} />}
      </PressButton>

      <Button
        variant="ghost"
        size="icon"
        className={large ? "size-11" : "size-10"}
        onClick={next}
        aria-label={t("player.nextTrack")}
        title={t("player.nextTrack")}
      >
        <NextIcon size={large ? 34 : 28} />
      </Button>

      <Button
        variant="ghost"
        size="icon"
        className={cn(large && "size-11", repeat !== "off" && "text-primary")}
        onClick={cycleRepeat}
        aria-label={repeatLabel}
        title={repeatLabel}
      >
        {repeat === "one" ? (
          <RepeatOneIcon size={large ? 24 : 20} />
        ) : (
          <RepeatIcon size={large ? 24 : 20} />
        )}
      </Button>
    </div>
  );
}
