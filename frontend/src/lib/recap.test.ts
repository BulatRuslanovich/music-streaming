// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import {
  busiestDay,
  daypartOf,
  dominantDaypart,
  intensity,
  leadingBlanks,
  monthKey,
  parseMonthKey,
} from "./recap";

describe("recap", () => {
  it("splits the day like the server does", () => {
    expect([4, 5, 10, 11, 16, 17, 22, 23].map(daypartOf)).toEqual([
      "night",
      "morning",
      "morning",
      "day",
      "day",
      "evening",
      "evening",
      "night",
    ]);
  });

  it("finds when most of the listening happened", () => {
    const hours = Array<number>(24).fill(0);
    hours[8] = 600;
    hours[21] = 400;
    hours[22] = 400;

    expect(dominantDaypart(hours)).toBe("evening");
    expect(dominantDaypart(Array<number>(24).fill(0))).toBeNull();
  });

  it("picks the earliest of equally busy days", () => {
    expect(busiestDay([0, 300, 900, 900])).toEqual({ day: 3, seconds: 900 });
    expect(busiestDay([0, 0])).toBeNull();
  });

  it("starts weeks on Monday", () => {
    // 1 сентября 2026 — вторник, 1 февраля 2026 — воскресенье.
    expect(leadingBlanks(2026, 9)).toBe(1);
    expect(leadingBlanks(2026, 2)).toBe(6);
  });

  it("lifts quiet days without flattening the busiest", () => {
    expect(intensity(0, 100)).toBe(0);
    expect(intensity(100, 100)).toBe(1);
    expect(intensity(25, 100)).toBe(0.5);
  });

  it("round-trips month keys and rejects nonsense", () => {
    expect(monthKey(2026, 9)).toBe("2026-09");
    expect(parseMonthKey("2026-09")).toEqual({ year: 2026, month: 9 });
    expect(parseMonthKey("2026-13")).toBeNull();
    expect(parseMonthKey(null)).toBeNull();
  });
});
