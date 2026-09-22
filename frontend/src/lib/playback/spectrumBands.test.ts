// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import {
  bandEdges,
  bandLevel,
  bandTilt,
  BYTES_PER_DB,
  MAX_HZ,
  MIN_HZ,
  SPECTRUM_BANDS,
} from "./spectrumBands";

describe("spectrum bands", () => {
  it.each([44_100, 48_000])("builds increasing edges at %i Hz", (sampleRate) => {
    const edges = bandEdges(sampleRate, 1024);

    expect(edges).toHaveLength(SPECTRUM_BANDS + 1);
    expect(edges.every((edge) => edge <= 1024)).toBe(true);
    for (let index = 1; index < edges.length; index += 1) {
      expect(edges[index]).toBeGreaterThan(edges[index - 1]);
    }
  });

  it("computes the expected increasing spectral tilt", () => {
    const tilt = bandTilt(3.5);

    expect(tilt[0]).toBeCloseTo(2, 1);
    expect(tilt[31]).toBeCloseTo(126.6, 1);
    for (let index = 1; index < tilt.length; index += 1) {
      expect(tilt[index]).toBeGreaterThan(tilt[index - 1]);
    }

    const centre = MIN_HZ * (MAX_HZ / MIN_HZ) ** (15.5 / SPECTRUM_BANDS);
    expect(tilt[15] / BYTES_PER_DB).toBeCloseTo(3.5 * Math.log2(centre / MIN_HZ), 5);
    expect([...bandTilt(0)]).toEqual(Array(SPECTRUM_BANDS).fill(0));
  });

  it("keeps silence silent and remains monotonic", () => {
    expect(bandLevel(0, 127)).toBe(0);
    expect(bandLevel(6, 127)).toBe(0);
    expect(bandLevel(255, 127)).toBe(1);

    let previous = bandLevel(0, 127);
    for (let peak = 1; peak <= 255; peak += 1) {
      const current = bandLevel(peak, 127);
      expect(current).toBeGreaterThanOrEqual(previous);
      previous = current;
    }
  });
});
