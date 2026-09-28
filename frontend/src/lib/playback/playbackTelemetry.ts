// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { recordEvent, type PlaybackSource } from "@/lib/events";
import type { Track } from "@/lib/types";

export interface PlaybackOrigin {
  source?: PlaybackSource;
}

export interface ListeningTracker {
  begin(track: Track, origin: PlaybackOrigin): void;
  accumulate(currentTime: number, origin: PlaybackOrigin): void;
  finish(type: "trackCompleted" | "trackSkipped", origin: PlaybackOrigin): void;
}

interface Played {
  trackId: string;
  seconds: number;
  position: number;
  duration: number;
}

const MAX_LISTENING_STEP_SECONDS = 2;

const HEARTBEAT_INTERVAL_SECONDS = 30;

const IDLE: Played = { trackId: "", seconds: 0, position: 0, duration: 0 };

/** Столько секунд надо послушать, чтобы трек попал в историю. Тот же порог у сервера: `HistoryService.ThresholdSeconds`. */
export const HISTORY_THRESHOLD_SECONDS = 30;

/**
 * Порог, после которого прослушивание попадает в историю. Он обрезается длиной трека: иначе
 * трек короче порога не попал бы туда никогда, сколько его ни слушай. За секунду до конца —
 * чтобы засчитать и дослушанный до конца короткий трек.
 */
export function historyThresholdFor(durationSeconds: number): number {
  return Math.min(HISTORY_THRESHOLD_SECONDS, Math.max(durationSeconds - 1, 1));
}

export function createListeningTracker(record = recordEvent): ListeningTracker {
  let played: Played = { ...IDLE };
  let heartbeatAt = 0;
  const heard = new Set<string>();

  const progressEvent = (
    type: "trackPlayed" | "trackCompleted" | "trackSkipped",
    origin: PlaybackOrigin,
  ) =>
    record({
      type,
      trackId: played.trackId,
      positionSeconds: Math.floor(played.position),
      listenedSeconds: Math.floor(played.seconds),
      durationSeconds: played.duration,
      ...origin,
    });

  return {
    begin(track, origin) {
      played = { trackId: track.id, seconds: 0, position: 0, duration: track.durationSeconds };
      heartbeatAt = 0;

      record({
        type: "trackStarted",
        trackId: track.id,
        durationSeconds: track.durationSeconds,
        ...origin,
      });

      if (heard.has(track.id)) {
        record({
          type: "trackReplayed",
          trackId: track.id,
          durationSeconds: track.durationSeconds,
          ...origin,
        });
      }

      heard.add(track.id);
    },

    accumulate(currentTime, origin) {
      if (!played.trackId) return;

      const delta = currentTime - played.position;
      if (delta > 0 && delta < MAX_LISTENING_STEP_SECONDS) played.seconds += delta;

      played.position = currentTime;

      if (played.seconds - heartbeatAt >= HEARTBEAT_INTERVAL_SECONDS) {
        heartbeatAt = played.seconds;
        progressEvent("trackPlayed", origin);
      }
    },

    finish(type, origin) {
      if (!played.trackId) return;

      progressEvent(type, origin);
      played = { ...IDLE };
    },
  };
}
