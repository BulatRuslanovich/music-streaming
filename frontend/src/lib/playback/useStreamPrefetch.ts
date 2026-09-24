// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { RefObject } from "react";
import { advanceIn } from "@/lib/playback/playerQueue";
import type { RepeatMode } from "@/lib/playback/playerTypes";
import {
  HEAD_START_SEGMENTS,
  STABLE_WINDOW_MS,
  pinStreamTracks,
  prefetchHlsTracks,
  prefetchStage,
} from "@/lib/playback/streamCache";
import type { Track } from "@/lib/types";
import { useSettings } from "@/contexts/SettingsContext";

const PREFETCH_RETRY_AFTER_MS = 10_000;

interface StreamPrefetchInput {
  currentTrack: Track | null;
  currentIndex: number;
  queue: Track[];
  orderRef: RefObject<number[]>;
  repeat: RepeatMode;
  online: boolean;
  isPlaying: boolean;
  position: number;
  buffered: number;
  duration: number;
}

// Греет кэш HLS на текущий и два следующих трека. Решение «как далеко забегать» целиком в
// prefetchStage, здесь — что именно качать под выбранную стадию и когда отступить.
export function useStreamPrefetch({
  currentTrack,
  currentIndex,
  queue,
  orderRef,
  repeat,
  online,
  isPlaying,
  position,
  buffered,
  duration,
}: StreamPrefetchInput): { noteStall: () => void } {
  const settings = useSettings();

  const prefetchRef = useRef<{ key: string; controller: AbortController } | null>(null);
  const prefetchRetryAtRef = useRef(0);
  const retryTimerRef = useRef<number | null>(null);

  const stallTimerRef = useRef<number | null>(null);

  // Захлёб — состояние, а не отметка времени в ref: оно входит в расчёт стадии, а стадия
  // считается на рендере. Окончание окна отсчитывает таймер, поэтому «пора снова качать»
  // наступает само, а не тогда, когда эффект случайно подняли по другой причине.
  const [stalledRecently, setStalledRecently] = useState(false);

  // Догрузку, отложенную на потом, некому разбудить: прогресс больше не тянет эффект за собой.
  // Поэтому окно ожидания заводит таймер, который и приводит эффект обратно.
  const [retryNudge, setRetryNudge] = useState(0);

  const deferRetry = useCallback(() => {
    prefetchRef.current = null;
    prefetchRetryAtRef.current = Date.now() + PREFETCH_RETRY_AFTER_MS;

    if (retryTimerRef.current !== null) window.clearTimeout(retryTimerRef.current);
    retryTimerRef.current = window.setTimeout(() => {
      retryTimerRef.current = null;
      setRetryNudge((nudge) => nudge + 1);
    }, PREFETCH_RETRY_AFTER_MS);
  }, []);

  // INFO: захлебнувшийся плеер — худший момент качать что-то ещё, поэтому текущую догрузку рвём.
  const noteStall = useCallback(() => {
    setStalledRecently(true);
    prefetchRef.current?.controller.abort();
    prefetchRef.current = null;

    if (stallTimerRef.current !== null) window.clearTimeout(stallTimerRef.current);
    stallTimerRef.current = window.setTimeout(() => {
      stallTimerRef.current = null;
      setStalledRecently(false);
    }, STABLE_WINDOW_MS);
  }, []);

  useEffect(
    () => () => {
      prefetchRef.current?.controller.abort();
      if (retryTimerRef.current !== null) window.clearTimeout(retryTimerRef.current);
      if (stallTimerRef.current !== null) window.clearTimeout(stallTimerRef.current);
    },
    [],
  );

  // Закрепление живёт отдельно от решения о догрузке: оно зависит только от того, какой трек
  // играет, а уходит сообщением в service worker. В общем эффекте оно повторялось на каждом
  // тике прогресса — несколько раз в секунду всю сессию, ради одного и того же значения.
  const currentTrackId = currentTrack?.id ?? null;

  useEffect(() => {
    pinStreamTracks(currentTrackId ? [currentTrackId] : []);
  }, [currentTrackId]);

  // Стадия — чистая функция от прогресса, и считать её на рендере дешевле, чем держать сам
  // прогресс в зависимостях: эффект поднимается на смену стадии, а не четырежды в секунду.
  const stage = prefetchStage({
    online,
    playing: isPlaying,
    position,
    bufferedUntil: buffered,
    duration,
    stalledRecently,
  });

  useEffect(() => {
    if (!currentTrack) {
      prefetchRef.current?.controller.abort();
      prefetchRef.current = null;
      prefetchRetryAtRef.current = 0;
      return;
    }

    const tracks = [currentTrack];
    if (repeat !== "one") {
      let index = currentIndex;
      for (let count = 0; count < 2; count += 1) {
        const step = advanceIn(orderRef.current, index, 1, repeat === "all");
        if (step.kind !== "play") break;
        const upcoming = queue[step.index];
        if (!upcoming || tracks.some((track) => track.id === upcoming.id)) break;
        tracks.push(upcoming);
        index = step.index;
      }
    }

    const reserveQuality = settings.dataSaver || settings.networkIsSlow ? "Low" : "Normal";

    // Разгон греет только начало следующего трека — это то, что убирает паузу на переходе, и
    // стоит десятков килобайт. Текущий трек в разгоне не трогаем: его и так тянет плеер.
    const headStart = stage === "headStart";
    const targets = headStart ? tracks.slice(1, 2) : tracks;
    const segmentLimit = headStart ? HEAD_START_SEGMENTS : undefined;

    const key = `${stage}:${reserveQuality}:${targets.map((track) => track.id).join(":")}`;

    if (prefetchRef.current?.key !== key) {
      prefetchRef.current?.controller.abort();
      prefetchRef.current = null;
      prefetchRetryAtRef.current = 0;
    }

    if (
      !settings.hlsEnabled ||
      prefetchRef.current ||
      Date.now() < prefetchRetryAtRef.current ||
      stage === "none" ||
      targets.length === 0
    ) {
      return;
    }

    const controller = new AbortController();
    prefetchRef.current = { key, controller };

    void prefetchHlsTracks(
      targets.map((track) => track.id),
      reserveQuality,
      controller.signal,
      segmentLimit,
    )
      .then((complete) => {
        if (!complete && prefetchRef.current?.controller === controller) deferRetry();
      })
      .catch(() => {
        if (prefetchRef.current?.controller === controller) deferRetry();
      });
  }, [
    currentTrack,
    currentIndex,
    queue,
    orderRef,
    repeat,
    stage,
    retryNudge,
    deferRetry,
    settings.hlsEnabled,
    settings.dataSaver,
    settings.networkIsSlow,
  ]);

  return { noteStall };
}
