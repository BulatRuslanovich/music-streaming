// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { PlaybackOrigin } from "@/lib/playback/playbackTelemetry";
import type { QueueSignals, RecommendationReason, Track } from "@/lib/types";

export type RepeatMode = "off" | "all" | "one";

export type RadioState = "idle" | "loading" | "empty" | "failed";

/** Радио, заведённое явно («радио от трека»): с ним очередь продолжается и без автопродолжения. */
export interface RadioSessionState {
  seedTrackId?: string | null;
  reasons: Record<string, RecommendationReason>;

  /** Пусто для сессий, восстановленных из хранилища до того, как сигналы стали приходить. */
  signals?: Record<string, QueueSignals>;
}

export type { PlaybackOrigin };

export interface QueueSnapshot {
  queue: Track[];
  order: number[];
  index: number;
  position: number;
  radioFrom: number;
  radioSession: RadioSessionState | null;
}

export interface PlayerState {
  queue: Track[];
  currentTrack: Track | null;
  /**
   * Что зазвучит следующим. Считается по порядку воспроизведения, а не по позиции в
   * очереди: под шаффлом `queue[currentIndex + 1]` — это соседняя строка списка, а не
   * следующий трек. `null`, когда очередь заканчивается на текущем.
   */
  nextTrack: Track | null;
  currentIndex: number;
  isPlaying: boolean;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  radio: RadioState;
  radioSession: RadioSessionState | null;
  radioStarting: boolean;
}

/**
 * Узкий срез состояния для списков и карточек: им нужно только «этот ли трек играет».
 * Отдельно от `PlayerState`, потому что тот меняется на каждый `patchTrack` (лайк) —
 * и перерисовывал бы все полки главной разом.
 */
export interface PlayerNowPlaying {
  currentTrackId: string | null;
  /** Нужен карточкам альбомов: они подсвечиваются, когда играет что угодно из альбома. */
  currentAlbumId: string | null;
  isPlaying: boolean;
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

  // INFO: стабильная пара к `getPosition` — нужна тем, кто не подписан на прогресс
  // (горячие клавиши в Player) и не должен из-за одной цифры перерисовываться 4 раза в секунду.
  getDuration: () => number;
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
  startRadio: (seedTrack?: Track | null) => Promise<boolean>;
}

export interface PlayerProgress {
  position: number;
  duration: number;
  buffered: number;

  getPosition: () => number;
}
