// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { readyToPrefetch } from "@/lib/streamCache";

const stable = {
  online: true,
  playing: true,
  position: 30,
  bufferedUntil: 90,
  duration: 240,
  lastStallAt: 0,
  now: 100_000,
};

describe("stream prefetch policy", () => {
  it("starts after sixty seconds are buffered", () => {
    expect(readyToPrefetch(stable)).toBe(true);
    expect(readyToPrefetch({ ...stable, bufferedUntil: 89.9 })).toBe(false);
  });

  it("accepts a fully buffered short remainder", () => {
    expect(readyToPrefetch({ ...stable, position: 220, bufferedUntil: 240, duration: 240 })).toBe(
      true,
    );
  });

  it("waits thirty seconds after a stall", () => {
    expect(readyToPrefetch({ ...stable, lastStallAt: 80_001 })).toBe(false);
    expect(readyToPrefetch({ ...stable, lastStallAt: 70_000 })).toBe(true);
  });

  it("does not prefetch while paused or offline", () => {
    expect(readyToPrefetch({ ...stable, playing: false })).toBe(false);
    expect(readyToPrefetch({ ...stable, online: false })).toBe(false);
  });
});
