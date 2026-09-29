// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback } from "react";
import type { Track } from "@/lib/types";
import { useNowPlaying, usePlayerActions } from "@/contexts/PlayerContext";

/**
 * «Играет — пауза, иначе — играй». Это правило было скопировано в девять мест (карточки,
 * плитки, полки, топ-результат поиска, геро-блок, кнопка полки, пронумерованные списки) и
 * успело разойтись: где-то попадание считалось по одному треку, где-то по всей очереди, а
 * карточка плейлиста и вовсе всегда рисовала иконку play, полагаясь на то, что клик
 * «всё равно распознается». Теперь правило одно.
 *
 * `playTrack` — для одного трека внутри списка: пауза, если играет именно он.
 * `playSet` — для набора целиком (альбом, плейлист, микс): пауза, если играет что-то из него.
 */
export function usePlayback() {
  const { currentTrackId, isPlaying } = useNowPlaying();
  const player = usePlayerActions();

  const playTrack = useCallback(
    (track: Track, context?: Track[]) => {
      if (currentTrackId === track.id) {
        player.toggle();
        return;
      }

      player.playTrack(track, context);
    },
    [currentTrackId, player],
  );

  const playSet = useCallback(
    (tracks: Track[], startIndex = 0) => {
      if (tracks.length === 0) return;

      const inQueue =
        currentTrackId !== null && tracks.some((track) => track.id === currentTrackId);

      if (inQueue) {
        player.toggle();
        return;
      }

      player.playQueue(tracks, startIndex);
    },
    [currentTrackId, player],
  );

  /** Играет ли прямо сейчас именно этот трек (а не просто выбран). */
  const soundingNow = useCallback(
    (trackId: string) => currentTrackId === trackId && isPlaying,
    [currentTrackId, isPlaying],
  );

  /** Звучит ли что-нибудь из этого набора — для кнопок «включить всё». */
  const setIsOnAir = useCallback(
    (tracks: Track[]) =>
      currentTrackId !== null && tracks.some((track) => track.id === currentTrackId),
    [currentTrackId],
  );

  return { currentTrackId, isPlaying, playTrack, playSet, soundingNow, setIsOnAir };
}
