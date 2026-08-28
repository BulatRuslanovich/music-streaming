// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { PlaybackRecovery } from "@/lib/playbackRecovery";
import { ADAPTIVE_COOLDOWN_STEPS_MS } from "@/lib/streamRecovery";
import type { AudioQuality, Track } from "@/lib/types";

const MEDIA_ERR_NETWORK = 2;

const MEDIA_ERR_DECODE = 3;

const QUALITIES = [{ quality: "Original" as AudioQuality }, { quality: "Low" as AudioQuality }];

function track(id = "t1", codec = "flac"): Track {
  return { id, codec } as Track;
}

function failing(recovery: PlaybackRecovery, errorCode: number, trackId = "t1") {
  return recovery.decide({
    trackId,
    errorCode,
    offline: false,
    fallbackTier: "Low",
    tier: "Original",
  });
}

describe("fail / recover", () => {
  it("reports only the first failure so the listener is told once", () => {
    const recovery = new PlaybackRecovery();

    expect(recovery.fail("t1", true)).toBe(true);
    expect(recovery.fail("t1", false)).toBe(false);
  });

  it("keeps the intent to listen sticky across repeated failures", () => {
    const recovery = new PlaybackRecovery();

    // Вторая ошибка прилетает уже на поставленном на паузу плеере — намерение не теряем.
    recovery.fail("t1", true);
    recovery.fail("t1", false);

    expect(recovery.recover()).toEqual({ resume: true });
  });

  it("has nothing to recover when nothing broke", () => {
    expect(new PlaybackRecovery().recover()).toBeNull();
  });

  it("recovers only once per failure", () => {
    const recovery = new PlaybackRecovery();
    recovery.fail("t1", true);

    expect(recovery.recover()).toEqual({ resume: true });
    expect(recovery.recover()).toBeNull();
  });

  it("forgets the failure when the source is rebuilt normally", () => {
    const recovery = new PlaybackRecovery();
    recovery.fail("t1", true);
    recovery.clearFailure();

    expect(recovery.recover()).toBeNull();
  });
});

describe("decide", () => {
  it("retries once before blaming the format — the first failure may be a stale session", () => {
    expect(failing(new PlaybackRecovery(), MEDIA_ERR_DECODE)).toMatchObject({
      kind: "retry",
      attempt: 0,
    });
  });

  it("falls back to a transcoded tier once a renewed session still cannot decode", () => {
    const recovery = new PlaybackRecovery();
    failing(recovery, MEDIA_ERR_DECODE);

    expect(failing(recovery, MEDIA_ERR_DECODE)).toEqual({ kind: "fallback", tier: "Low" });
  });

  it("remembers the fallback so the same track does not fall back twice", () => {
    const recovery = new PlaybackRecovery();
    failing(recovery, MEDIA_ERR_DECODE);
    failing(recovery, MEDIA_ERR_DECODE);

    // Откатываться больше некуда — дальше только ждать перекодировку.
    expect(failing(recovery, MEDIA_ERR_DECODE).kind).toBe("retry");
  });

  it("serves the fallback tier for a track that already fell back", () => {
    const recovery = new PlaybackRecovery();
    failing(recovery, MEDIA_ERR_DECODE);
    failing(recovery, MEDIA_ERR_DECODE);

    expect(recovery.tierFor(track(), "Original", QUALITIES, "Low")).toBe("Low");
  });

  it("counts attempts up so retries back off instead of hammering", () => {
    const recovery = new PlaybackRecovery();

    const first = failing(recovery, MEDIA_ERR_NETWORK);
    const second = failing(recovery, MEDIA_ERR_NETWORK);

    expect(first).toMatchObject({ kind: "retry", attempt: 0 });
    expect(second).toMatchObject({ kind: "retry", attempt: 1 });
  });

  it("starts counting from scratch on a different track", () => {
    const recovery = new PlaybackRecovery();
    failing(recovery, MEDIA_ERR_NETWORK, "t1");

    expect(failing(recovery, MEDIA_ERR_NETWORK, "t2")).toMatchObject({ attempt: 0 });
  });

  it("drops the attempt count once sound actually starts", () => {
    const recovery = new PlaybackRecovery();
    failing(recovery, MEDIA_ERR_NETWORK);
    recovery.playing();

    expect(failing(recovery, MEDIA_ERR_NETWORK)).toMatchObject({ attempt: 0 });
  });

  it("waits for the network instead of burning retries while offline", () => {
    const recovery = new PlaybackRecovery();

    expect(
      recovery.decide({
        trackId: "t1",
        errorCode: MEDIA_ERR_NETWORK,
        offline: true,
        fallbackTier: "Low",
        tier: "Original",
      }),
    ).toEqual({ kind: "offline" });
  });
});

describe("adaptive degradation", () => {
  it("leaves chosen tiers alone — only the original is degraded", () => {
    const recovery = new PlaybackRecovery();

    expect(recovery.forceAdaptive("Low", true, "t1")).toBe(false);
  });

  it("switches the original to adaptive delivery on a slow network", () => {
    const recovery = new PlaybackRecovery();

    expect(recovery.forceAdaptive("Original", true, "t1")).toBe(true);
  });

  it("keeps a track on adaptive delivery once it has been moved there", () => {
    const recovery = new PlaybackRecovery();
    recovery.forceAdaptive("Original", true, "t1");

    // Сеть «выправилась», но прыгать туда-сюда на том же треке нельзя.
    expect(recovery.forceAdaptive("Original", false, "t1")).toBe(true);
  });

  it("grows the cool-down with every degradation", () => {
    const recovery = new PlaybackRecovery();
    const now = 1_000_000;

    recovery.degrade(now);
    expect(recovery.coolingDown(now + ADAPTIVE_COOLDOWN_STEPS_MS[0] - 1)).toBe(true);
    expect(recovery.coolingDown(now + ADAPTIVE_COOLDOWN_STEPS_MS[0] + 1)).toBe(false);

    recovery.degrade(now);
    expect(recovery.coolingDown(now + ADAPTIVE_COOLDOWN_STEPS_MS[0] + 1)).toBe(true);
  });

  it("wipes the fallback history when the listener changes quality", () => {
    const recovery = new PlaybackRecovery();
    failing(recovery, MEDIA_ERR_DECODE);
    failing(recovery, MEDIA_ERR_DECODE);

    recovery.reset();

    expect(recovery.tierFor(track(), "Original", QUALITIES, "Low")).toBe("Original");
  });

  it("keeps the cool-down through a quality change — the network is still slow", () => {
    const recovery = new PlaybackRecovery();
    const now = 1_000_000;
    recovery.degrade(now);

    recovery.reset();

    expect(recovery.coolingDown(now + 1)).toBe(true);
    // Но счётчик деградаций обнулён, поэтому следующая выдержка снова самая короткая.
    recovery.degrade(now);
    expect(recovery.coolingDown(now + ADAPTIVE_COOLDOWN_STEPS_MS[0] + 1)).toBe(false);
  });
});
