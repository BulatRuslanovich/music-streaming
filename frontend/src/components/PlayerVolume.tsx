// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Seekbar } from "./Seekbar";
import { Button } from "./ui/button";
import { MuteIcon, VolumeIcon } from "./Icons";

/**
 * Кнопка звука и ползунок громкости — одинаковые в футере и на полном экране.
 *
 * Обёртки вокруг нет намеренно: в футере она несёт `ref` для колёсика мыши, на полном экране
 * задаёт свою ширину. Различается только раскладка, поэтому она остаётся у вызывающего.
 */
export function PlayerVolume({
  size = "icon",
  seekbarClassName,
}: {
  size?: "icon" | "icon-lg";
  seekbarClassName?: string;
}) {
  const player = usePlayer();
  const t = useT();

  const silent = player.muted || player.volume === 0;

  return (
    <>
      <Button
        variant="ghost"
        size={size}
        onClick={player.toggleMute}
        aria-label={player.muted ? t("player.unmute") : t("player.mute")}
      >
        {silent ? <MuteIcon size={20} /> : <VolumeIcon size={20} />}
      </Button>

      <Seekbar
        value={player.muted ? 0 : player.volume}
        max={1}
        step={0.01}
        onSeek={player.setVolume}
        ariaLabel={t("player.volume")}
        className={cn("volume-seek", seekbarClassName)}
      />
    </>
  );
}
