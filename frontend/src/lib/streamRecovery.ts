// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { AudioQuality } from "@/lib/types";

export const STREAM_RETRY_DELAYS_MS = [800, 2500, 6000];

export const TRANSCODE_WAIT_DELAYS_MS = [1500, 4000, 9000, 18000];

export const ADAPTIVE_COOLDOWN_STEPS_MS = [5 * 60_000, 15 * 60_000, 45 * 60_000, 120 * 60_000];

export function adaptiveCooldownMs(previousDegradations: number): number {
  const step = Math.max(0, Math.min(previousDegradations, ADAPTIVE_COOLDOWN_STEPS_MS.length - 1));
  return ADAPTIVE_COOLDOWN_STEPS_MS[step];
}

const MEDIA_ERR_DECODE = 3;

const MEDIA_ERR_SRC_NOT_SUPPORTED = 4;

export type Recovery =
  | { kind: "fallback"; tier: AudioQuality }
  | { kind: "unsupported" }
  | { kind: "retry"; tier: AudioQuality; attempt: number; delayMs: number }
  | { kind: "giveUp" };

export function decideRecovery({
  errorCode,
  tier,
  fallbackTier,
  fellBack,
  attempts,
  sessionRenewed = true,
}: {
  errorCode?: number;
  tier: AudioQuality;
  fallbackTier: AudioQuality | null;
  fellBack: boolean;
  attempts: number;
  sessionRenewed?: boolean;
}): Recovery {
  const undecodable = errorCode === MEDIA_ERR_DECODE || errorCode === MEDIA_ERR_SRC_NOT_SUPPORTED;

  if (undecodable && tier === "Original" && !fellBack && sessionRenewed) {
    return fallbackTier ? { kind: "fallback", tier: fallbackTier } : { kind: "unsupported" };
  }

  const delays = fellBack ? TRANSCODE_WAIT_DELAYS_MS : STREAM_RETRY_DELAYS_MS;
  if (attempts >= delays.length) return { kind: "giveUp" };

  return { kind: "retry", tier, attempt: attempts, delayMs: delays[attempts] };
}
