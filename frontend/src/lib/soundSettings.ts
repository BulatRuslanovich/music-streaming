// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useSyncExternalStore } from "react";

export interface SoundSettings {
  normalization: "off" | "track" | "album";
  transition: "off" | "crossfade" | "gapless";
  crossfadeSeconds: number;
}
const defaults: SoundSettings = { normalization: "off", transition: "off", crossfadeSeconds: 4 };
const key = "caimack.sound";
let cachedRaw: string | null = null;
let cached = defaults;
const listeners = new Set<() => void>();

export function parseSoundSettings(raw: string | null): SoundSettings {
  try {
    const value = JSON.parse(raw ?? "null");
    return {
      normalization:
        value?.normalization === "track" || value?.normalization === "album"
          ? value.normalization
          : "off",
      transition:
        value?.transition === "crossfade" || value?.transition === "gapless"
          ? value.transition
          : "off",
      crossfadeSeconds: Number.isFinite(value?.crossfadeSeconds)
        ? Math.max(1, Math.min(12, value.crossfadeSeconds))
        : 4,
    };
  } catch {
    return defaults;
  }
}
function read() {
  try {
    const raw = localStorage.getItem(key);
    if (raw !== cachedRaw) {
      cachedRaw = raw;
      cached = parseSoundSettings(raw);
    }
  } catch {}
  return cached;
}
function subscribe(listener: () => void) {
  listeners.add(listener);
  window.addEventListener("storage", listener);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", listener);
  };
}
export function useSoundSettings() {
  return useSyncExternalStore(subscribe, read, () => defaults);
}
export function updateSoundSettings(changes: Partial<SoundSettings>) {
  cached = { ...read(), ...changes };
  cachedRaw = JSON.stringify(cached);
  try {
    localStorage.setItem(key, cachedRaw);
  } catch {}
  listeners.forEach((listener) => listener());
}
