// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { RadioSessionState } from "@/lib/playback/playerTypes";
import type { QueueSignals, RecommendationReason, RecommendedTrack, Track } from "@/lib/types";

export function recommendationReasons(
  items: RecommendedTrack[],
): Record<string, RecommendationReason> {
  return Object.fromEntries(items.map((item) => [item.track.id, item.reason]));
}

export function queueSignals(items: RecommendedTrack[]): Record<string, QueueSignals> {
  return Object.fromEntries(
    items.filter((item) => item.signals).map((item) => [item.track.id, item.signals!]),
  );
}

export function mergeRadioBatch(
  queue: Track[],
  reasons: Record<string, RecommendationReason>,
  signals: Record<string, QueueSignals>,
  items: RecommendedTrack[],
): {
  tracks: Track[];
  reasons: Record<string, RecommendationReason>;
  signals: Record<string, QueueSignals>;
} {
  const known = new Set(queue.map((track) => track.id));
  const fresh = items.filter((item) => !known.has(item.track.id));

  return {
    tracks: fresh.map((item) => item.track),
    reasons: { ...reasons, ...recommendationReasons(fresh) },
    signals: { ...signals, ...queueSignals(fresh) },
  };
}

export function validRadioSession(value: unknown): value is RadioSessionState {
  if (!value || typeof value !== "object") return false;

  const candidate = value as Partial<RadioSessionState>;
  return typeof candidate.reasons === "object" && candidate.reasons !== null;
}
