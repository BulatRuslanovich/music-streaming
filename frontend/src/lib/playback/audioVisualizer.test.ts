// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { afterEach, describe, expect, it, vi } from "vitest";

type FrameCallback = (timestamp: number) => void;

describe("audio visualizer track transitions", () => {
  afterEach(() => {
    vi.resetModules();
    vi.unstubAllGlobals();
  });

  it("recovers when a new track starts after inter-track silence", async () => {
    let peak = 100;
    let nextFrame: FrameCallback | null = null;
    let frameId = 0;

    const analyser = {
      connect: vi.fn(),
      fftSize: 0,
      frequencyBinCount: 1024,
      getByteFrequencyData: vi.fn((bins: Uint8Array) => bins.fill(peak)),
      maxDecibels: 0,
      minDecibels: 0,
      smoothingTimeConstant: 0,
    };

    class FakeAudioContext {
      readonly destination = {};
      readonly sampleRate = 48_000;

      createAnalyser() {
        return analyser;
      }

      createGain() {
        return { connect: vi.fn(), gain: { value: 1 } };
      }

      createMediaElementSource() {
        return { connect: vi.fn() };
      }

      resume() {
        return Promise.resolve();
      }
    }

    vi.stubGlobal("AudioContext", FakeAudioContext);
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    vi.stubGlobal("requestAnimationFrame", (callback: FrameCallback) => {
      nextFrame = callback;
      frameId += 1;
      return frameId;
    });

    const { visualizer } = await import("./audioVisualizer");
    const audio = {
      currentTime: 30,
      muted: false,
      paused: false,
      volume: 1,
    } as HTMLAudioElement;

    visualizer.attach(audio);
    visualizer.setTrack("first");
    visualizer.setPlaying(true);
    const frames: number[] = [];
    const unsubscribe = visualizer.subscribe((levels) => frames.push(levels[0]));

    let timestamp = 1;
    const advance = () => {
      const callback = nextFrame;
      expect(callback).not.toBeNull();
      nextFrame = null;
      callback?.(timestamp);
      timestamp += 100;
    };

    advance();
    expect(frames.at(-1)).toBeGreaterThan(0);

    // Старый media element ещё выглядит играющим, пока следующий источник загружается.
    peak = 0;
    for (let frame = 0; frame < 30; frame += 1) advance();

    // Новый трек запускается на том же audio element и будит уже построенный граф.
    audio.currentTime = 0;
    peak = 100;
    visualizer.setTrack("second");
    audio.currentTime = 0.1;
    visualizer.refresh();

    expect(visualizer.available).toBe(true);
    advance();
    expect(frames.at(-1)).toBeGreaterThan(0);

    unsubscribe();
  });
});
