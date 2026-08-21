// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { PlaybackOrigin } from "@/lib/playbackTelemetry";
import type { DjMode, DjVariety, RecommendationReason, Track } from "@/lib/types";

export type RepeatMode = "off" | "all" | "one";

export type RadioState = "idle" | "loading" | "empty" | "failed";

export interface DjSessionState {
  mode: DjMode;
  variety: DjVariety;
  seedTrackId?: string | null;
  status: RadioState;
  reasons: Record<string, RecommendationReason>;
}

export type { PlaybackOrigin };

export interface QueueSnapshot {
  queue: Track[];
  order: number[];
  index: number;
  position: number;
  radioFrom: number;
  dj: DjSessionState | null;
}

export interface PlayerState {
  queue: Track[];
  currentTrack: Track | null;
  currentIndex: number;
  isPlaying: boolean;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  radio: RadioState;
  dj: DjSessionState | null;
  djLoading: boolean;
}

export interface PlayerActions {
  playQueue: (tracks: Track[], startIndex?: number, origin?: PlaybackOrigin) => void;
  playTrack: (track: Track, contextTracks?: Track[], origin?: PlaybackOrigin) => void;
  toggle: () => void;
  pause: () => void;
  next: () => void;
  previous: () => void;
  seek: (seconds: number) => void;
  seekBy: (deltaSeconds: number) => void;
  setVolume: (volume: number) => void;
  toggleMute: () => void;
  toggleShuffle: () => void;
  cycleRepeat: () => void;
  addToQueue: (track: Track) => void;
  playNext: (track: Track) => void;
  removeFromQueue: (index: number) => void;
  moveInQueue: (from: number, to: number) => void;
  clearQueue: () => void;
  jumpTo: (index: number) => void;
  patchTrack: (trackId: string, changes: Partial<Track>) => void;
  snapshotQueue: () => QueueSnapshot;
  restoreQueue: (snapshot: QueueSnapshot) => void;
  startDj: (mode: DjMode, seedTrack?: Track | null) => Promise<boolean>;
  setDjVariety: (variety: DjVariety) => void;
}

export interface PlayerProgress {
  position: number;
  duration: number;
  buffered: number;

  // `position` follows the browser's throttled timeupdate event. Call this when sub-second timing
  // matters, for example while choosing the active synchronized lyric line.
  getPosition: () => number;
}
