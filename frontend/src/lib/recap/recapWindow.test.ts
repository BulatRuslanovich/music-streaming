// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { RECAP_WINDOW_DAYS, recapWindow } from "./recapWindow";

describe("recap window", () => {
  it("opens for the first week of the month and closes after it", () => {
    const onDay = (day: number) =>
      recapWindow(new Date(`2026-09-${String(day).padStart(2, "0")}T12:00:00Z`), "UTC");

    expect(onDay(1).open).toBe(true);
    expect(onDay(RECAP_WINDOW_DAYS).open).toBe(true);
    expect(onDay(RECAP_WINDOW_DAYS + 1).open).toBe(false);
    expect(onDay(30).open).toBe(false);
  });

  it("always names the previous month, including across a year boundary", () => {
    expect(recapWindow(new Date("2026-09-03T12:00:00Z"), "UTC").month).toBe("2026-08");
    expect(recapWindow(new Date("2026-01-02T12:00:00Z"), "UTC").month).toBe("2025-12");
    expect(recapWindow(new Date("2026-11-05T12:00:00Z"), "UTC").month).toBe("2026-10");
  });

  it("reads the calendar in the listener's zone, not in UTC", () => {
    // 20:00 UTC 31 августа — в Окленде уже первое сентября, окно открыто.
    const evening = new Date("2026-08-31T20:00:00Z");
    expect(recapWindow(evening, "Pacific/Auckland")).toEqual({ open: true, month: "2026-08" });
    expect(recapWindow(evening, "UTC").open).toBe(false);

    // 02:00 UTC 8 сентября — в Лос-Анджелесе ещё седьмое, окно не закрылось.
    const earlyMorning = new Date("2026-09-08T02:00:00Z");
    expect(recapWindow(earlyMorning, "America/Los_Angeles").open).toBe(true);
    expect(recapWindow(earlyMorning, "UTC").open).toBe(false);
  });

  it("falls back to the browser clock when the zone is unknown", () => {
    const now = new Date("2026-09-03T12:00:00Z");
    const fallback = recapWindow(now, "Mars/Olympus");

    expect(fallback).toEqual({
      open: now.getDate() <= RECAP_WINDOW_DAYS,
      month: recapWindow(now, Intl.DateTimeFormat().resolvedOptions().timeZone).month,
    });
  });
});
