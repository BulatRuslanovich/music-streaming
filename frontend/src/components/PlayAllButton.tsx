// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Track } from "@/lib/types";
import { usePlayback } from "@/lib/usePlayback";
import { useT } from "@/contexts/I18nContext";
import { PressButton } from "./ui/button";
import { PauseIcon, PlayIcon } from "./Icons";

export function PlayAllButton({ tracks, name }: { tracks: Track[]; name?: string }) {
  const t = useT();
  const { isPlaying, playSet, setIsOnAir } = usePlayback();

  const playing = setIsOnAir(tracks) && isPlaying;

  return (
    <PressButton
      variant="play"
      size="play"
      disabled={tracks.length === 0}
      onClick={() => playSet(tracks)}
      aria-label={
        playing ? t("action.pause") : name ? t("action.playNamed", { name }) : t("action.play")
      }
    >
      {playing ? <PauseIcon size={20} /> : <PlayIcon size={20} />}
    </PressButton>
  );
}
