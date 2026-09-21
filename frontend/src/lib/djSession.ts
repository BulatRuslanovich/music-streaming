// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { DjSessionState } from "@/lib/playerTypes";
import type {
  DjMode,
  DjVariety,
  QueueSignals,
  RecommendationReason,
  RecommendedTrack,
  Track,
} from "@/lib/types";

export function defaultDjVariety(mode: DjMode): DjVariety {
  return mode === "Discover" || mode === "DeepCuts" ? "Adventurous" : "Balanced";
}

export function recommendationReasons(
  items: RecommendedTrack[],
): Record<string, RecommendationReason> {
  return Object.fromEntries(items.map((item) => [item.track.id, item.reason]));
}

/** Сигналы есть не у всех режимов, поэтому треки без них в карту просто не попадают. */
export function queueSignals(items: RecommendedTrack[]): Record<string, QueueSignals> {
  return Object.fromEntries(
    items.filter((item) => item.signals).map((item) => [item.track.id, item.signals!]),
  );
}

export function mergeDjBatch(
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

export function validDjSession(value: unknown): value is DjSessionState {
  if (!value || typeof value !== "object") return false;

  const candidate = value as Partial<DjSessionState>;
  return (
    ["ForYou", "Rediscover", "Discover", "Flow", "DeepCuts"].includes(candidate.mode ?? "") &&
    ["Familiar", "Balanced", "Adventurous"].includes(candidate.variety ?? "") &&
    ["idle", "loading", "empty", "failed"].includes(candidate.status ?? "") &&
    typeof candidate.reasons === "object" &&
    candidate.reasons !== null
  );
}
