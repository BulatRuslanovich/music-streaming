// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it, vi } from "vitest";
import {
  AdaptivePlayback,
  adaptiveCap,
  choosePlaybackTransport,
} from "@/lib/playback/adaptivePlayback";

describe("adaptive playback selection", () => {
  const original = {
    quality: "Original",
    originalPlayable: true,
    forceAdaptive: false,
  } as const;

  it("keeps a decodable original on the progressive stream", () => {
    expect(choosePlaybackTransport(original, true)).toBe("progressive");
  });

  it("uses hls.js for quality tiers and degraded originals", () => {
    for (const quality of ["Low", "Normal", "High"] as const) {
      expect(choosePlaybackTransport({ ...original, quality }, true)).toBe("hls.js");
    }

    expect(choosePlaybackTransport({ ...original, forceAdaptive: true }, true)).toBe("hls.js");
  });

  it("moves an original the browser cannot decode to hls.js", () => {
    expect(choosePlaybackTransport({ ...original, originalPlayable: false }, true)).toBe("hls.js");
  });

  it("falls back to the original without hls.js", () => {
    const request = { ...original, quality: "Normal" as const };

    expect(choosePlaybackTransport(request, false)).toBe("progressive");
  });

  it("caps an adaptive original at the high rendition", () => {
    expect(adaptiveCap("Original")).toBe("High");
    expect(adaptiveCap("Normal")).toBe("Normal");
  });
});

describe("a destroyed AdaptivePlayback", () => {
  /**
   * Элемент `<audio>` один на весь плеер, а экземпляров AdaptivePlayback — по одному на
   * загрузку. Счётчик поколений разводит загрузки внутри экземпляра и ничего не знает про
   * соседей, поэтому `load`, долетевший после `destroy` (резолв источника из IndexedDB
   * успевает отстать от переключения трека), ставил `src` предыдущего трека поверх нового.
   */
  // Набор идёт в node-окружении, без DOM. Плееру здесь нужна только та горстка членов, до
  // которой доходит прогрессивная ветка загрузки, плюс константа готовности из HTMLMediaElement.
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
    // Прямой поток — это всегда оригинал, без параметра качества.
    expect(audio.src).toBe(`/api/tracks/${request.trackId}/stream`);
  });
});
