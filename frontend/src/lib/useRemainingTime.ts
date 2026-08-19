// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useSyncExternalStore } from "react";

const STORAGE_KEY = "music-streaming.remaining";

const listeners = new Set<() => void>();

let value = false;
let hydrated = false;

function notify() {
  for (const listener of listeners) listener();
}

function subscribe(listener: () => void) {
  if (!hydrated) {
    hydrated = true;

    try {
      const stored = window.localStorage.getItem(STORAGE_KEY) === "1";
      if (stored !== value) {
        value = stored;
        queueMicrotask(notify);
      }
    } catch {}
  }

  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function toggleRemainingTime() {
  value = !value;

  try {
    window.localStorage.setItem(STORAGE_KEY, value ? "1" : "0");
  } catch {}

  notify();
}

export function useRemainingTime(): boolean {
  return useSyncExternalStore(
    subscribe,
    () => value,
    () => false,
  );
}
