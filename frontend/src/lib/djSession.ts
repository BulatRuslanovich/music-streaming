// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { DjSessionState } from "@/lib/playerTypes";
import type { DjMode, DjVariety, RecommendationReason, RecommendedTrack, Track } from "@/lib/types";

export function defaultDjVariety(mode: DjMode): DjVariety {
  return mode === "Discover" || mode === "DeepCuts" ? "Adventurous" : "Balanced";
}

export function recommendationReasons(
  items: RecommendedTrack[],
): Record<string, RecommendationReason> {
  return Object.fromEntries(items.map((item) => [item.track.id, item.reason]));
}

export function mergeDjBatch(
  queue: Track[],
  reasons: Record<string, RecommendationReason>,
  items: RecommendedTrack[],
): { tracks: Track[]; reasons: Record<string, RecommendationReason> } {
  const known = new Set(queue.map((track) => track.id));
  const fresh = items.filter((item) => !known.has(item.track.id));

  return {
    tracks: fresh.map((item) => item.track),
    reasons: { ...reasons, ...recommendationReasons(fresh) },
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
