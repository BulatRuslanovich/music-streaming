// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useMemo } from "react";
import type { Track } from "@/lib/types";
import { useNowPlaying, usePlayerActions, type PlaybackOrigin } from "@/contexts/PlayerContext";

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
export function usePlayback(origin?: PlaybackOrigin) {
  const { currentTrackId, isPlaying } = useNowPlaying();
  const player = usePlayerActions();

  // Вызывающие передают origin литералом, то есть новым объектом на каждый рендер.
  // Раскладываем на примитивы, иначе колбэки не удержать стабильными.
  const source = origin?.source;
  const sourceId = origin?.sourceId;
  const target = useMemo<PlaybackOrigin | undefined>(
    () => (source || sourceId ? { source, sourceId } : undefined),
    [source, sourceId],
  );

  const playTrack = useCallback(
    (track: Track, context?: Track[]) => {
      if (currentTrackId === track.id) {
        player.toggle();
        return;
      }

      player.playTrack(track, context, target);
    },
    [currentTrackId, player, target],
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

      player.playQueue(tracks, startIndex, target);
    },
    [currentTrackId, player, target],
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
