// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Track } from "./catalog";

export interface RecommendationReason {
  kind: string;
  subject?: string | null;
  subjectId?: string | null;
}

/**
 * Чем очередь радио руководствовалась, ставя сюда именно этот трек. Приходит только с
 * радио: остальные полки собираются иначе, и этих чисел у них нет.
 */
export interface QueueSignals {
  explore: boolean;
}

export interface RecommendedTrack {
  track: Track;
  reason: RecommendationReason;
  score?: number | null;
  signals?: QueueSignals | null;
}

export interface RadioBatch {
  tracks: RecommendedTrack[];
  seedTrackId?: string | null;
}
