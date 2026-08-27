// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useRef } from "react";
import type { RefObject } from "react";
import { advanceIn } from "@/lib/playerQueue";
import type { RepeatMode } from "@/lib/playerTypes";
import { pinStreamTracks, prefetchHlsTracks, readyToPrefetch } from "@/lib/streamCache";
import type { Track } from "@/lib/types";
import { useSettings } from "@/contexts/SettingsContext";

const PREFETCH_RETRY_AFTER_MS = 10_000;

export interface StreamPrefetchInput {
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

// Греет кэш HLS на текущий и два следующих трека, но только когда сеть явно справляется:
// решение «пора ли» целиком в readyToPrefetch, здесь — что именно качать и когда отступить.
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
  const lastStallAtRef = useRef(0);

  // INFO: захлебнувшийся плеер — худший момент качать что-то ещё, поэтому текущую догрузку рвём.
  const noteStall = useCallback(() => {
    lastStallAtRef.current = Date.now();
    prefetchRef.current?.controller.abort();
    prefetchRef.current = null;
  }, []);

  useEffect(() => () => prefetchRef.current?.controller.abort(), []);

  useEffect(() => {
    if (!currentTrack) {
      pinStreamTracks([]);
      prefetchRef.current?.controller.abort();
      prefetchRef.current = null;
      prefetchRetryAtRef.current = 0;
      return;
    }

    pinStreamTracks([currentTrack.id]);

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
    const key = `${reserveQuality}:${tracks.map((track) => track.id).join(":")}`;

    if (prefetchRef.current?.key !== key) {
      prefetchRef.current?.controller.abort();
      prefetchRef.current = null;
      prefetchRetryAtRef.current = 0;
    }

    if (
      !settings.hlsEnabled ||
      prefetchRef.current ||
      Date.now() < prefetchRetryAtRef.current ||
      !readyToPrefetch({
        online,
        playing: isPlaying,
        position,
        bufferedUntil: buffered,
        duration,
        lastStallAt: lastStallAtRef.current,
        now: Date.now(),
      })
    ) {
      return;
    }

    const controller = new AbortController();
    prefetchRef.current = { key, controller };

    void prefetchHlsTracks(
      tracks.map((track) => track.id),
      reserveQuality,
      controller.signal,
    )
      .then((complete) => {
        if (!complete && prefetchRef.current?.controller === controller) {
          prefetchRef.current = null;
          prefetchRetryAtRef.current = Date.now() + PREFETCH_RETRY_AFTER_MS;
        }
      })
      .catch(() => {
        if (prefetchRef.current?.controller === controller) {
          prefetchRef.current = null;
          prefetchRetryAtRef.current = Date.now() + PREFETCH_RETRY_AFTER_MS;
        }
      });
  }, [
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
    settings.hlsEnabled,
    settings.dataSaver,
    settings.networkIsSlow,
  ]);

  return { noteStall };
}
