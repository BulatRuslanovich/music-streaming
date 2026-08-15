"use client";

import { useEffect, useRef } from "react";
import type { RepeatMode } from "@/contexts/PlayerContext";
import type { Track } from "@/lib/types";

const STORAGE_KEY = "music-streaming.player";

const POSITION_SAVE_INTERVAL_MS = 10_000;

export interface PersistedPlayer {
  queue: Track[];
  index: number;
  position: number;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
}

export function readPersistedPlayer(): Partial<PersistedPlayer> | null {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as Partial<PersistedPlayer>) : null;
  } catch {
    window.localStorage.removeItem(STORAGE_KEY);
    return null;
  }
}

export function usePersistedPlayer(snapshot: PersistedPlayer, ready: boolean, isPlaying: boolean) {
  const latest = useRef(snapshot);

  useEffect(() => {
    latest.current = snapshot;
  });

  const { queue, index, volume, muted, shuffle, repeat } = snapshot;

  useEffect(() => {
    if (ready) write(latest.current);
  }, [ready, queue, index, volume, muted, shuffle, repeat]);

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
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(snapshot));
  } catch {}
}
