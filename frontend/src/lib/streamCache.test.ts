// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { prefetchStage } from "@/lib/streamCache";

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
  it("goes all in once sixty seconds are buffered", () => {
    expect(prefetchStage(stable)).toBe("full");
    expect(prefetchStage({ ...stable, bufferedUntil: 89.9 })).toBe("headStart");
  });

  it("accepts a fully buffered short remainder", () => {
    expect(prefetchStage({ ...stable, position: 220, bufferedUntil: 240, duration: 240 })).toBe(
      "full",
    );
  });

  // Раньше здесь был единственный порог в шестьдесят секунд, и на узком канале префетч
  // не запускался вообще — то есть отсутствовал ровно там, где нужнее всего.
  it("still warms the head start when the buffer is thin", () => {
    expect(prefetchStage({ ...stable, bufferedUntil: 31 })).toBe("headStart");
    expect(prefetchStage({ ...stable, bufferedUntil: 30 })).toBe("headStart");
  });

  it("waits thirty seconds after a stall", () => {
    expect(prefetchStage({ ...stable, lastStallAt: 80_001 })).toBe("none");
    expect(prefetchStage({ ...stable, lastStallAt: 70_000 })).toBe("full");
  });

  it("does not prefetch while paused or offline", () => {
    expect(prefetchStage({ ...stable, playing: false })).toBe("none");
    expect(prefetchStage({ ...stable, online: false })).toBe("none");
  });
});
