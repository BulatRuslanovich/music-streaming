import type { AudioQuality } from "@/lib/types";

export const STREAM_RETRY_DELAYS_MS = [800, 2500, 6000];

export const TRANSCODE_WAIT_DELAYS_MS = [1500, 4000, 9000, 18000];

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
}: {
  errorCode?: number;
  tier: AudioQuality;
  fallbackTier: AudioQuality | null;
  fellBack: boolean;
  attempts: number;
}): Recovery {
  const undecodable = errorCode === MEDIA_ERR_DECODE || errorCode === MEDIA_ERR_SRC_NOT_SUPPORTED;

  if (undecodable && tier === "Original" && !fellBack) {
    return fallbackTier ? { kind: "fallback", tier: fallbackTier } : { kind: "unsupported" };
  }

  const delays = fellBack ? TRANSCODE_WAIT_DELAYS_MS : STREAM_RETRY_DELAYS_MS;
  if (attempts >= delays.length) return { kind: "giveUp" };

  return { kind: "retry", tier, attempt: attempts, delayMs: delays[attempts] };
}
