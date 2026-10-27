// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { AdaptivePlayback } from "@/lib/playback/adaptivePlayback";
import type { AudioQuality } from "@/lib/types";

export function sourceKeyOf(
  trackId: string,
  quality: AudioQuality,
  forceAdaptive: boolean,
  revision: number,
): string {
  return `${trackId}:${quality}:${forceAdaptive ? "adaptive" : "direct"}:${revision}`;
}

export function silence(audio: HTMLAudioElement, playback: AdaptivePlayback | null): void {
  playback?.destroy();
  audio.pause();
  audio.removeAttribute("src");
  audio.load();

  delete audio.dataset.trackId;
  delete audio.dataset.sourceKey;
  delete audio.dataset.sourceLoading;
  delete audio.dataset.playbackMode;
}
