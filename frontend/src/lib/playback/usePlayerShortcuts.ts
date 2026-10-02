// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef } from "react";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import {
  isTypingTarget,
  resolveShortcut,
  shortcutAcceptsRepeat,
  shortcutNeedsTrack,
} from "@/lib/shortcuts";
import { useToggleFavorite } from "@/lib/useToggleFavorite";

export function usePlayerShortcuts(toggleQueue: () => void): void {
  const state = usePlayerState();
  const actions = usePlayerActions();
  const toggleFavorite = useToggleFavorite();

  const handle = (event: KeyboardEvent) => {
    if (isTypingTarget(event.target)) return;

    const hit = resolveShortcut(event);
    if (!hit) return;

    const inOverlay =
      document.querySelector(
        "[data-state='open'][role='dialog']:not([data-player-fullscreen]), [data-state='open'][role='menu']",
      ) !== null;
    if (inOverlay) return;

    const { currentTrack } = state;
    if (!currentTrack && shortcutNeedsTrack(hit.action)) return;

    event.preventDefault();
    if (event.repeat && !shortcutAcceptsRepeat(hit.action)) return;

    switch (hit.action) {
      case "playPause":
        actions.toggle();
        break;
      case "seekBy":
        actions.seekBy(hit.value ?? 0);
        break;
      case "next":
        actions.next();
        break;
      case "previous":
        actions.previous();
        break;
      case "volumeBy":
        actions.setVolume((state.muted ? 0 : state.volume) + (hit.value ?? 0));
        break;
      case "mute":
        actions.toggleMute();
        break;
      case "shuffle":
        actions.toggleShuffle();
        break;
      case "repeat":
        actions.cycleRepeat();
        break;
      case "queue":
        toggleQueue();
        break;
    }
  };

  const latest = useRef(handle);

  useEffect(() => {
    latest.current = handle;
  });

  useEffect(() => {
    const listener = (event: KeyboardEvent) => latest.current(event);

    window.addEventListener("keydown", listener);
    return () => window.removeEventListener("keydown", listener);
  }, []);
}
