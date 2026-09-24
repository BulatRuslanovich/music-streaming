// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { prefetchStage } from "@/lib/playback/streamCache";

const stable = {
  online: true,
  playing: true,
  position: 30,
  bufferedUntil: 90,
  duration: 240,
  stalledRecently: false,
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

  // Единственный порог в шестьдесят секунд означал бы, что на узком канале префетч
  // не запускался вообще — то есть отсутствовал ровно там, где нужнее всего.
  it("still warms the head start when the buffer is thin", () => {
    expect(prefetchStage({ ...stable, bufferedUntil: 31 })).toBe("headStart");
    expect(prefetchStage({ ...stable, bufferedUntil: 30 })).toBe("headStart");
  });

  // Само окно теперь отсчитывает таймер в useStreamPrefetch: сюда приходит уже готовый ответ,
  // и проверять здесь остаётся только то, что захлёб перекрывает любую стадию.
  it("holds off entirely while a stall is still recent", () => {
    expect(prefetchStage({ ...stable, stalledRecently: true })).toBe("none");
    expect(prefetchStage({ ...stable, stalledRecently: false })).toBe("full");
    expect(prefetchStage({ ...stable, bufferedUntil: 31, stalledRecently: true })).toBe("none");
  });

  it("does not prefetch while paused or offline", () => {
    expect(prefetchStage({ ...stable, playing: false })).toBe("none");
    expect(prefetchStage({ ...stable, online: false })).toBe("none");
  });
});
