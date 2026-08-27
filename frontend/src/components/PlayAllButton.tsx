// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { Track } from "@/lib/types";
import { useNowPlaying, usePlayerActions } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { PressButton } from "./ui/button";
import { PauseIcon, PlayIcon } from "./Icons";

export function PlayAllButton({ tracks, name }: { tracks: Track[]; name?: string }) {
  const t = useT();
  const { currentTrackId, isPlaying } = useNowPlaying();
  const player = usePlayerActions();

  const isThisQueue =
    tracks.length > 0 &&
    currentTrackId !== null &&
    tracks.some((track) => track.id === currentTrackId);

  const playing = isThisQueue && isPlaying;

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
