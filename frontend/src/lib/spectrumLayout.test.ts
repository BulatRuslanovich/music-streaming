// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { bandPositions, barCount, sampleAt } from "./spectrumLayout";

describe("spectrum layout", () => {
  it("sizes an even number of bars within the supported range", () => {
    expect(barCount(0)).toBe(0);
    expect(barCount(604)).toBe(66);
    expect(barCount(1144)).toBe(126);
    expect(barCount(1624)).toBe(180);
    expect(barCount(3440)).toBe(256);

    for (const width of [1, 604, 1144, 1624, 3440]) {
      const bars = barCount(width);
      expect(bars).toBeGreaterThanOrEqual(64);
      expect(bars).toBeLessThanOrEqual(256);
      expect(bars % 2).toBe(0);
    }
  });

  it("mirrors every band without dropping any of them", () => {
    const positions = bandPositions(180, true);

    expect(positions[0]).toBe(0);
    expect(positions[89]).toBe(31);
    expect(positions[90]).toBe(31);
    expect(new Set(positions.map(Math.round)).size).toBe(32);
    for (let index = 0; index < positions.length; index += 1) {
      expect(positions[index]).toBeCloseTo(positions[positions.length - 1 - index]);
    }
  });

  it("samples exact, interpolated, and clamped positions", () => {
    const levels = [0, 0.25, 1];

    expect(sampleAt(levels, 1)).toBe(0.25);
    expect(sampleAt(levels, 1.5)).toBe(0.625);
    expect(sampleAt(levels, 99)).toBe(1);
    expect(sampleAt(levels, -1)).toBe(0);
  });
});
