import { recordEvent, type PlaybackSource } from "@/lib/events";
import type { Track } from "@/lib/types";

export interface PlaybackOrigin {
  source?: PlaybackSource;
  sourceId?: string;
}

export interface ListeningTracker {
  begin(track: Track, origin: PlaybackOrigin): void;
  accumulate(currentTime: number, origin: PlaybackOrigin): void;
  pause(origin: PlaybackOrigin): void;
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

export function createListeningTracker(record = recordEvent): ListeningTracker {
  let played: Played = { ...IDLE };
  let heartbeatAt = 0;
  const heard = new Set<string>();

  const progressEvent = (
    type: "trackPlayed" | "trackPaused" | "trackCompleted" | "trackSkipped",
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

    pause(origin) {
      if (!played.trackId) return;
      progressEvent("trackPaused", origin);
    },

    finish(type, origin) {
      if (!played.trackId) return;

      progressEvent(type, origin);
      played = { ...IDLE };
    },
  };
}
