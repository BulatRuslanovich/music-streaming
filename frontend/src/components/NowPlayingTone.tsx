// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect } from "react";
import { trackCoverUrl } from "@/lib/media";
import { toneStyle, useCoverTone } from "@/lib/useCoverTone";
import { usePlayerState } from "@/contexts/PlayerContext";

// Тон того, что играет, — на <html>: им подкрашены плеер и свечение над содержимым.
export function NowPlayingTone() {
  const { currentTrack } = usePlayerState();
  const tone = useCoverTone(currentTrack ? trackCoverUrl(currentTrack, "thumb") : null);

  useEffect(() => {
    if (tone === undefined) return;

    const root = document.documentElement;
    for (const [name, value] of Object.entries(toneStyle(tone))) {
      root.style.setProperty(name, String(value));
    }
  }, [tone]);

  return null;
}
