import { describe, expect, it } from "vitest";
import {
  decideRecovery,
  STREAM_RETRY_DELAYS_MS,
  TRANSCODE_WAIT_DELAYS_MS,
} from "@/lib/streamRecovery";

const MEDIA_ERR_NETWORK = 2;

const MEDIA_ERR_DECODE = 3;

const MEDIA_ERR_SRC_NOT_SUPPORTED = 4;

const base = {
  tier: "Original",
  fallbackTier: "Low",
  fellBack: false,
  attempts: 0,
} as const;

describe("decideRecovery", () => {
  it("falls back to a transcoded tier when the browser cannot decode the original", () => {
    for (const errorCode of [MEDIA_ERR_DECODE, MEDIA_ERR_SRC_NOT_SUPPORTED]) {
      expect(decideRecovery({ ...base, errorCode })).toEqual({ kind: "fallback", tier: "Low" });
    }
  });

  it("gives up on the format when there is nothing to fall back to", () => {
    expect(decideRecovery({ ...base, errorCode: MEDIA_ERR_DECODE, fallbackTier: null })).toEqual({
      kind: "unsupported",
    });
  });

  it("does not fall back twice for the same track", () => {
    const recovery = decideRecovery({ ...base, errorCode: MEDIA_ERR_DECODE, fellBack: true });

    expect(recovery).toEqual({
      kind: "retry",
      tier: "Original",
      attempt: 0,
      delayMs: TRANSCODE_WAIT_DELAYS_MS[0],
    });
  });

  it("does not fall back when already playing a transcoded tier", () => {
    const recovery = decideRecovery({ ...base, errorCode: MEDIA_ERR_DECODE, tier: "Low" });

    expect(recovery).toEqual({
      kind: "retry",
      tier: "Low",
      attempt: 0,
      delayMs: STREAM_RETRY_DELAYS_MS[0],
    });
  });

  it("retries network errors on the same tier", () => {
    for (const [attempt, delayMs] of STREAM_RETRY_DELAYS_MS.entries()) {
      expect(decideRecovery({ ...base, errorCode: MEDIA_ERR_NETWORK, attempts: attempt })).toEqual({
        kind: "retry",
        tier: "Original",
        attempt,
        delayMs,
      });
    }
  });

  it("waits longer between retries while a transcode is being prepared", () => {
    for (const [attempt, delayMs] of TRANSCODE_WAIT_DELAYS_MS.entries()) {
      const recovery = decideRecovery({
        ...base,
        errorCode: MEDIA_ERR_NETWORK,
        fellBack: true,
        attempts: attempt,
      });

      expect(recovery).toEqual({ kind: "retry", tier: "Original", attempt, delayMs });
    }
  });

  it("stops after the last delay", () => {
    expect(
      decideRecovery({
        ...base,
        errorCode: MEDIA_ERR_NETWORK,
        attempts: STREAM_RETRY_DELAYS_MS.length,
      }),
    ).toEqual({ kind: "giveUp" });

    expect(
      decideRecovery({
        ...base,
        errorCode: MEDIA_ERR_NETWORK,
        fellBack: true,
        attempts: TRANSCODE_WAIT_DELAYS_MS.length,
      }),
    ).toEqual({ kind: "giveUp" });
  });

  it("retries when the element reports no error code", () => {
    expect(decideRecovery({ ...base, errorCode: undefined })).toMatchObject({ kind: "retry" });
  });
});
