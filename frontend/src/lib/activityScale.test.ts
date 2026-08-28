// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { densifyDays, intensityOf, parseLocalDate, weekdayIndex } from "@/lib/activityScale";

describe("densifyDays", () => {
  const today = new Date();
  today.setHours(0, 0, 0, 0);

  function isoDay(offset: number): string {
    const date = new Date(today);
    date.setDate(date.getDate() + offset);

    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");

    return `${date.getFullYear()}-${month}-${day}`;
  }

  it("fills the gaps the server leaves out", () => {
    // Сервер группирует по дням и молчит про те, в которые не слушали.
    const filled = densifyDays([
      { date: isoDay(-4), listenedSeconds: 100, plays: 2 },
      { date: isoDay(-1), listenedSeconds: 300, plays: 5 },
    ]);

    expect(filled.map((day) => day.listenedSeconds)).toEqual([100, 0, 0, 300, 0]);
    expect(filled.map((day) => day.date)).toEqual([
      isoDay(-4),
      isoDay(-3),
      isoDay(-2),
      isoDay(-1),
      isoDay(0),
    ]);
  });

  it("starts from the period boundary rather than the first day with music", () => {
    const filled = densifyDays([{ date: isoDay(-1), listenedSeconds: 60, plays: 1 }], isoDay(-3));

    expect(filled).toHaveLength(4);
    expect(filled[0]).toEqual({ date: isoDay(-3), listenedSeconds: 0, plays: 0 });
  });

  it("leaves an empty period empty", () => {
    expect(densifyDays([])).toEqual([]);
  });
});

describe("intensityOf", () => {
  it("keeps an empty day distinguishable from a quiet one", () => {
    expect(intensityOf(0, 100)).toBe(0);
    expect(intensityOf(1, 100)).toBe(1);
  });

  it("spreads levels by root so one marathon does not flatten the rest", () => {
    // По корню день на 10% от пика уже вторая ступень; по линейной шкале он был бы первой,
    // вместе со всеми днями до 25%, и месяц выглядел бы одинаково бледным.
    expect(intensityOf(5, 100)).toBe(1);
    expect(intensityOf(10, 100)).toBe(2);
    expect(intensityOf(40, 100)).toBe(3);
    expect(intensityOf(80, 100)).toBe(4);
  });

  it("has no level to give when nothing was listened to at all", () => {
    expect(intensityOf(0, 0)).toBe(0);
  });
});

describe("parseLocalDate", () => {
  it("reads a plain date as local midnight, not UTC", () => {
    // `new Date("2026-05-01")` — это UTC-полночь, и западнее Гринвича она вчерашняя.
    const date = parseLocalDate("2026-05-01");

    expect(date.getFullYear()).toBe(2026);
    expect(date.getMonth()).toBe(4);
    expect(date.getDate()).toBe(1);
  });

  it("tolerates a full timestamp", () => {
    expect(parseLocalDate("2026-05-01T21:00:00+03:00").getDate()).toBe(1);
  });
});

describe("weekdayIndex", () => {
  it("starts the week on Monday", () => {
    expect(weekdayIndex(parseLocalDate("2024-01-01"))).toBe(0);
    expect(weekdayIndex(parseLocalDate("2024-01-07"))).toBe(6);
  });
});
