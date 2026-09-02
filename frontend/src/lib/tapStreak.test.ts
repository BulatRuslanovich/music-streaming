// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { nextStreak } from "@/lib/tapStreak";

describe("nextStreak", () => {
  const WINDOW = 500;

  it("starts the count at the first tap", () => {
    expect(nextStreak(null, 1000, WINDOW)).toBe(1);
  });

  it("continues a series while taps stay inside the window", () => {
    let streak = { count: nextStreak(null, 0, WINDOW), at: 0 };

    for (const at of [200, 400, 600, 800, 1000, 1200]) {
      streak = { count: nextStreak(streak, at, WINDOW), at };
    }

    expect(streak.count).toBe(7);
  });

  it("restarts once a tap arrives after the window", () => {
    const previous = { count: 6, at: 1000 };

    expect(nextStreak(previous, 1501, WINDOW)).toBe(1);
  });

  it("keeps a tap exactly on the window boundary", () => {
    const previous = { count: 3, at: 1000 };

    expect(nextStreak(previous, 1500, WINDOW)).toBe(4);
  });
});
