// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Track } from "@/lib/types";
import { usePlayer } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { PressButton } from "./ui/button";
import { PauseIcon, PlayIcon } from "./Icons";

export function PlayAllButton({ tracks, name }: { tracks: Track[]; name?: string }) {
  const t = useT();
  const player = usePlayer();

  const isThisQueue =
    tracks.length > 0 &&
    player.currentTrack !== null &&
    tracks.some((track) => track.id === player.currentTrack?.id);

  const playing = isThisQueue && player.isPlaying;

  return (
    <PressButton
      variant="play"
      size="play"
      disabled={tracks.length === 0}
      onClick={() => {
        if (isThisQueue) {
          player.toggle();
          return;
        }
        player.playQueue(tracks, 0);
      }}
      aria-label={
        playing ? t("action.pause") : name ? t("action.playNamed", { name }) : t("action.play")
      }
    >
      {playing ? <PauseIcon size={22} /> : <PlayIcon size={22} />}
    </PressButton>
  );
}
