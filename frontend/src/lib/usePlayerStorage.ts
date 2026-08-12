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
  dataSaver: boolean;
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

/**
 * Writes the snapshot when the structural state changes, and only every ten seconds while a track
 * plays. Saving on every position update would stringify the whole queue four times a second.
 */
export function usePersistedPlayer(snapshot: PersistedPlayer, ready: boolean, isPlaying: boolean) {
  const latest = useRef(snapshot);

  useEffect(() => {
    latest.current = snapshot;
  });

  const { queue, index, volume, muted, shuffle, repeat, dataSaver } = snapshot;

  useEffect(() => {
    if (ready) write(latest.current);
  }, [ready, queue, index, volume, muted, shuffle, repeat, dataSaver]);

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
