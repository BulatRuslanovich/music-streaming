// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { comparisonPeriod, periodDelta } from "@/lib/statisticsDelta";
import type { DailyActivity } from "@/lib/types";

const TODAY = new Date(2026, 4, 20);

function day(date: string, listenedSeconds: number): DailyActivity {
  return { date, listenedSeconds, plays: 1 };
}

/** Локальная дата как `YYYY-MM-DD`: `toISOString` дал бы UTC и сдвинул день восточнее Гринвича. */
function isoDay(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");

  return `${date.getFullYear()}-${month}-${day}`;
}

/** Ровно `seconds` в каждый из `count` дней, заканчивая на `endsOn` включительно. */
function run(endsOn: string, count: number, seconds: number): DailyActivity[] {
  const end = new Date(`${endsOn}T00:00:00`);

  return Array.from({ length: count }, (_, offset) => {
    const date = new Date(end);
    date.setDate(date.getDate() - offset);

    return day(isoDay(date), seconds);
  }).reverse();
}

describe("comparisonPeriod", () => {
  it("compares a week against the month around it and a month against the quarter", () => {
    expect(comparisonPeriod("Week")).toBe("Month");
    expect(comparisonPeriod("Month")).toBe("Quarter");
  });

  it("refuses to compare periods without a fixed-length window", () => {
    // Год считается от 1 января, поэтому в январе он короче квартала — сравнивать нечем.
    expect(comparisonPeriod("Quarter")).toBeNull();
    expect(comparisonPeriod("Year")).toBeNull();
    expect(comparisonPeriod("All")).toBeNull();
  });
});

describe("periodDelta", () => {
  it("measures the last seven days against the seven before them", () => {
    const days = [...run("2026-05-13", 7, 100), ...run("2026-05-20", 7, 200)];

    expect(periodDelta("Week", days, TODAY)).toEqual({
      current: 1400,
      previous: 700,
      percent: 100,
    });
  });

  it("reports a fall as a negative percentage", () => {
    const days = [...run("2026-05-13", 7, 200), ...run("2026-05-20", 7, 150)];

    expect(periodDelta("Week", days, TODAY)?.percent).toBe(-25);
  });

  it("ignores days outside both windows", () => {
    const days = [
      day("2026-01-01", 99_999),
      ...run("2026-05-13", 7, 100),
      ...run("2026-05-20", 7, 100),
    ];

    expect(periodDelta("Week", days, TODAY)).toEqual({
      current: 700,
      previous: 700,
      percent: 0,
    });
  });

  it("gives no comparison when the previous window is empty", () => {
    // Рост «с нуля» в процентах не выражается — показывать тут нечего.
    expect(periodDelta("Week", run("2026-05-20", 7, 100), TODAY)).toBeNull();
  });

  it("gives no comparison for periods without a window, whatever the data", () => {
    const days = [...run("2026-05-13", 60, 100), ...run("2026-05-20", 60, 100)];

    expect(periodDelta("Year", days, TODAY)).toBeNull();
    expect(periodDelta("All", days, TODAY)).toBeNull();
  });

  it("counts a thirty-day month against the thirty days before it", () => {
    const days = [...run("2026-04-20", 30, 60), ...run("2026-05-20", 30, 90)];

    expect(periodDelta("Month", days, TODAY)).toEqual({
      current: 2700,
      previous: 1800,
      percent: 50,
    });
  });
});
