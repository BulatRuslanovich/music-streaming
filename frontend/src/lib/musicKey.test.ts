// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { camelot, pitchClass } from "@/lib/musicKey";

describe("pitchClass", () => {
  it("names every step of the octave", () => {
    const names = Array.from({ length: 12 }, (_, key) => pitchClass(key));

    expect(names).toEqual(["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"]);
  });

  it("stays silent when the analyser was not sure", () => {
    expect(pitchClass(null)).toBeNull();
    expect(pitchClass(undefined)).toBeNull();
  });

  it("refuses a step outside the octave", () => {
    expect(pitchClass(12)).toBeNull();
    expect(pitchClass(-1)).toBeNull();
    expect(pitchClass(1.5)).toBeNull();
  });
});

describe("camelot", () => {
  it("places the majors on the wheel", () => {
    // C мажор — 8B, и дальше по квинтам: G 9B, D 10B, A 11B.
    expect(camelot(0, false)).toBe("8B");
    expect(camelot(7, false)).toBe("9B");
    expect(camelot(2, false)).toBe("10B");
    expect(camelot(9, false)).toBe("11B");
    expect(camelot(11, false)).toBe("1B");
  });

  it("puts a minor on the same number as its relative major", () => {
    // A минор делит номер с C мажором, E минор — с G мажором.
    expect(camelot(9, true)).toBe("8A");
    expect(camelot(4, true)).toBe("9A");
    expect(camelot(0, true)).toBe("5A");
    expect(camelot(8, true)).toBe("1A");
  });

  it("covers the whole wheel exactly once per mode", () => {
    const majors = new Set(Array.from({ length: 12 }, (_, key) => camelot(key, false)));
    const minors = new Set(Array.from({ length: 12 }, (_, key) => camelot(key, true)));

    expect(majors.size).toBe(12);
    expect(minors.size).toBe(12);
  });

  it("stays silent without a key", () => {
    expect(camelot(null, false)).toBeNull();
    expect(camelot(12, true)).toBeNull();
  });
});
