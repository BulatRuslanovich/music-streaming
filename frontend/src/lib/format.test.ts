// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { calendarDaysAgo } from "@/lib/format";

describe("calendarDaysAgo", () => {
  const now = new Date(2026, 8, 29, 14, 30);

  it("counts a moment earlier today as today", () => {
    expect(calendarDaysAgo(new Date(2026, 8, 29, 0, 5), now)).toBe(0);
  });

  it("counts yesterday evening as one day ago, however few hours have passed", () => {
    const justAfterMidnight = new Date(2026, 8, 29, 0, 1);
    expect(calendarDaysAgo(new Date(2026, 8, 28, 23, 59), justAfterMidnight)).toBe(1);
  });

  it("counts whole calendar days back across a month boundary", () => {
    expect(calendarDaysAgo(new Date(2026, 7, 30, 9, 0), now)).toBe(30);
    expect(calendarDaysAgo(new Date(2026, 8, 22, 23, 0), now)).toBe(7);
  });

  it("returns a negative count for a moment in the future", () => {
    expect(calendarDaysAgo(new Date(2026, 8, 30, 1, 0), now)).toBe(-1);
  });
});
