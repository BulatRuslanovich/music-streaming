// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it, vi } from "vitest";
import { AdaptivePlayback, adaptiveWanted } from "@/lib/playback/adaptivePlayback";

describe("adaptive playback selection", () => {
  const original = {
    quality: "Original",
    originalPlayable: true,
    forceAdaptive: false,
  } as const;

  it("keeps a decodable original on the progressive stream", () => {
    expect(adaptiveWanted(original)).toBe(false);
  });

  it("goes adaptive for quality tiers, degraded originals and undecodable formats", () => {
    for (const quality of ["Low", "Normal"] as const) {
      expect(adaptiveWanted({ ...original, quality })).toBe(true);
    }

    expect(adaptiveWanted({ ...original, forceAdaptive: true })).toBe(true);
    expect(adaptiveWanted({ ...original, originalPlayable: false })).toBe(true);
  });
});

describe("a destroyed AdaptivePlayback", () => {
  function stubAudio() {
    (globalThis as { HTMLMediaElement?: unknown }).HTMLMediaElement ??= { HAVE_METADATA: 1 };

    return {
      dataset: {} as Record<string, string>,
      src: "",
      readyState: 0,
      currentTime: 0,
      pause: vi.fn(),
      load: vi.fn(),
      play: vi.fn(() => Promise.resolve()),
      removeAttribute: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      canPlayType: vi.fn(() => ""),
    };
  }

  const request = {
    trackId: "11111111-1111-7111-8111-111111111111",
    codec: "mp3",
    quality: "Original" as const,
    forceAdaptive: false,
    slowNetwork: false,
    startAt: 12,
    play: true,
  };

  it("does not touch the shared audio element", async () => {
    const audio = stubAudio();
    const playback = new AdaptivePlayback(audio as unknown as HTMLAudioElement, {
      onFatalError: () => {},
    });

    playback.destroy();
    await playback.load(request);

    expect(audio.pause).not.toHaveBeenCalled();
    expect(audio.load).not.toHaveBeenCalled();
    expect(audio.src).toBe("");
    expect(audio.dataset).toEqual({});
  });

  it("still settles, so callers can move on", async () => {
    const audio = stubAudio();
    const playback = new AdaptivePlayback(audio as unknown as HTMLAudioElement, {
      onFatalError: () => {},
    });

    playback.destroy();

    await expect(playback.load(request)).resolves.toBeUndefined();
  });

  it("loads normally until it is destroyed", async () => {
    const audio = stubAudio();
    const playback = new AdaptivePlayback(audio as unknown as HTMLAudioElement, {
      onFatalError: () => {},
    });

    await playback.load(request);

    expect(audio.pause).toHaveBeenCalled();
    expect(audio.src).toBe(`/api/tracks/${request.trackId}/stream`);
  });
});
