// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { afterEach, describe, expect, it, vi } from "vitest";
import { listeningChange, monthLabel } from "./recap";
import { parseSoundSettings } from "./soundSettings";

describe("recap and audio settings", () => {
  it("does not invent a percentage without a previous month", () => {
    expect(listeningChange(100, 0)).toBeNull();
    expect(listeningChange(300, 200)).toBe(50);
    expect(monthLabel("2026-08", "en-US")).toBe("August 2026");
  });
  it("recovers from damaged settings and bounds crossfade duration", () => {
    expect(parseSoundSettings("broken").transition).toBe("off");
    expect(parseSoundSettings('{"crossfadeSeconds":999}').crossfadeSeconds).toBe(12);
    expect(parseSoundSettings('{"transition":"unknown"}').transition).toBe("off");
  });
});

describe("audio clock transitions", () => {
  afterEach(() => {
    vi.resetModules();
    vi.unstubAllGlobals();
  });

  async function setup() {
    const starts: { when: number; offset: number; stopped: boolean }[] = [];
    const clock = { currentTime: 0 };
    class Context {
      destination = {};
      get currentTime() {
        return clock.currentTime;
      }
      createGain() {
        return {
          connect: vi.fn(),
          disconnect: vi.fn(),
          gain: {
            value: 1,
            setValueAtTime: vi.fn(),
            linearRampToValueAtTime: vi.fn(),
            setTargetAtTime: vi.fn(),
          },
        };
      }
      createBufferSource() {
        let entry: (typeof starts)[number];
        return {
          buffer: null,
          onended: null,
          connect: vi.fn(),
          disconnect: vi.fn(),
          start: (when: number, offset: number) => {
            entry = { when, offset, stopped: false };
            starts.push(entry);
          },
          stop: () => {
            if (entry) entry.stopped = true;
          },
        };
      }
    }
    vi.stubGlobal("AudioContext", Context);
    const { BufferedPlayback } = await import("./bufferedPlayback");
    const playback = new BufferedPlayback();
    const buffer = { duration: 10 } as AudioBuffer;
    return { playback, buffer, clock, starts };
  }

  it("schedules gapless playback exactly at the decoded boundary", async () => {
    const { playback, buffer, clock, starts } = await setup();
    playback.load("first", buffer, 3, true, 1);
    playback.prepare("next", buffer, 1, "gapless", 4);
    expect(starts.map(({ when, offset }) => [when, offset])).toEqual([
      [0, 3],
      [7, 0],
    ]);
    clock.currentTime = 6.99;
    expect(playback.tick()).toBeNull();
    clock.currentTime = 7.05;
    expect(playback.tick()).toBe("transition");
    expect(playback.trackId).toBe("next");
    expect(playback.position).toBeCloseTo(0.05);
    expect(playback.tick()).toBeNull();
  });

  it("crossfades before the boundary and cancels future audio when paused", async () => {
    const { playback, buffer, clock, starts } = await setup();
    playback.load("first", buffer, 0, true, 1);
    playback.prepare("next", buffer, 0.5, "crossfade", 4);
    expect(starts[1].when).toBe(6);
    clock.currentTime = 2;
    playback.pause();
    expect(starts.every((entry) => entry.stopped)).toBe(true);
    clock.currentTime = 20;
    expect(playback.position).toBe(2);
    expect(playback.tick()).toBeNull();
    playback.play();
    expect(starts.at(-1)?.when).toBe(24);
    clock.currentTime = 24;
    expect(playback.tick()).toBe("transition");
  });

  it("seeking and replacing the next track leave no stale scheduled sources", async () => {
    const { playback, buffer, starts } = await setup();
    playback.load("first", buffer, 0, true, 1);
    playback.prepare("wrong", buffer, 1, "gapless", 0);
    playback.cancelNext();
    expect(starts[1].stopped).toBe(true);
    playback.seek(8);
    playback.prepare("right", buffer, 1, "gapless", 0);
    expect(starts.at(-1)?.when).toBe(2);
    playback.stop();
    expect(starts.every((entry) => entry.stopped)).toBe(true);
    expect(playback.trackId).toBeUndefined();
  });
});
