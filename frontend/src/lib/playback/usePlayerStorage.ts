// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef } from "react";
import { readStoredJson, writeStoredJson } from "@/lib/storage";
import type { RadioSessionState, RepeatMode } from "@/lib/playback/playerTypes";
import type { Track } from "@/lib/types";

const STORAGE_KEY = "music-streaming.player";

const POSITION_SAVE_INTERVAL_MS = 10_000;

interface PersistedPlayer {
  queue: Track[];
  index: number;
  position: number;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  radioSession?: RadioSessionState | null;
}

export function readPersistedPlayer(): Partial<PersistedPlayer> | null {
  const stored = readStoredJson(STORAGE_KEY);
  return stored !== null && typeof stored === "object"
    ? (stored as Partial<PersistedPlayer>)
    : null;
}

export function usePersistedPlayer(snapshot: PersistedPlayer, ready: boolean, isPlaying: boolean) {
  const latest = useRef(snapshot);

  useEffect(() => {
    latest.current = snapshot;
  });

  const { queue, index, volume, muted, shuffle, repeat, radioSession } = snapshot;

  useEffect(() => {
    if (ready) write(latest.current);
  }, [ready, queue, index, volume, muted, shuffle, repeat, radioSession]);

  useEffect(() => {
    if (!ready) return;

    const save = () => write(latest.current);
    window.addEventListener("pagehide", save);

    return () => {
      window.removeEventListener("pagehide", save);
      save();
    };
  }, [ready]);

  useEffect(() => {
    if (!ready || !isPlaying) return;

    const timer = window.setInterval(() => write(latest.current), POSITION_SAVE_INTERVAL_MS);

    return () => {
      window.clearInterval(timer);
      write(latest.current);
    };
  }, [ready, isPlaying]);
}

function write(snapshot: PersistedPlayer) {
  writeStoredJson(STORAGE_KEY, snapshot);
}
