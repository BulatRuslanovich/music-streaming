// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef } from "react";
import { validRadioSession } from "@/lib/playback/radioSession";
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

/** Сохранённое состояние, приведённое к допустимому: хранилище могла писать старая версия. */
export function readPersistedPlayer(): PersistedPlayer | null {
  const stored = readStoredJson(STORAGE_KEY);
  if (stored === null || typeof stored !== "object") return null;

  const saved = stored as Partial<PersistedPlayer>;
  const queue = Array.isArray(saved.queue) ? saved.queue : [];
  const index = typeof saved.index === "number" ? saved.index : 0;

  return {
    queue,
    index: index >= 0 && index < queue.length ? index : -1,
    position: typeof saved.position === "number" ? saved.position : 0,
    volume: typeof saved.volume === "number" ? saved.volume : 1,
    muted: saved.muted === true,
    shuffle: saved.shuffle === true,
    repeat: saved.repeat === "all" || saved.repeat === "one" ? saved.repeat : "off",
    radioSession: validRadioSession(saved.radioSession) ? saved.radioSession : null,
  };
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
