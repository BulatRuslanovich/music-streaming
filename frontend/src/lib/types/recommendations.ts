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
 * радио и диджея: остальные полки собираются иначе, и этих чисел у них нет.
 */
export interface QueueSignals {
  explore: boolean;
  newBoost: boolean;
  cosineTaste: number;
  cosineCurrent: number;
  clusterId?: number | null;
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

export type SuppressionTarget = "track" | "artist";

export interface RecommendationSuppression {
  target: SuppressionTarget;
  targetId: string;
  createdAt: string;
  expiresAt?: string | null;
}

// DeepCuts нет в UI: режим находят через палитру команд, а не выбирают на главной.
export type DjMode = "ForYou" | "Rediscover" | "Discover" | "Flow" | "DeepCuts";

export type DjVariety = "Familiar" | "Balanced" | "Adventurous";

export interface DjBatch {
  mode: DjMode;
  variety: DjVariety;
  seedTrackId?: string | null;
  tracks: RecommendedTrack[];
}
