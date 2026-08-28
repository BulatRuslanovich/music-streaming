// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { DailyActivity, StatisticsPeriod } from "@/lib/types";
import { parseLocalDate } from "@/lib/activityScale";

/**
 * Сравнение периода с предыдущим таким же. «12 ч 30 мин» само по себе не значит ничего —
 * это много или мало, видно только рядом с прошлым месяцем.
 *
 * Отдельной ручки под это у API нет: период задаётся перечислением, произвольное окно не
 * запросить. Поэтому берём период на ступень шире (в нём заведомо помещаются оба окна) и
 * режем его `byDay` сами.
 */

/** Длина окна в днях. Год и «всё время» — окна плавающей длины, их не с чем сравнивать. */
const WINDOW_DAYS: Partial<Record<StatisticsPeriod, number>> = {
  Week: 7,
  Month: 30,
  Quarter: 90,
};

/**
 * Период, из которого берём данные для сравнения. Для квартала им был бы «год», но год
 * считается от 1 января: в январе он короче самого квартала, и сравнение оказалось бы
 * выдумкой. Поэтому квартал сравнения не получает.
 */
const WIDER: Partial<Record<StatisticsPeriod, StatisticsPeriod>> = {
  Week: "Month",
  Month: "Quarter",
};

export function comparisonPeriod(period: StatisticsPeriod): StatisticsPeriod | null {
  return WIDER[period] ?? null;
}

export interface PeriodDelta {
  current: number;
  previous: number;
  /** Изменение в процентах, округлённое. Положительное — рост. */
  percent: number;
}

/**
 * `days` берётся из `byDay` периода на ступень шире. Возвращает `null`, когда сравнивать
 * не с чем: нет данных, период без окна фиксированной длины, или в прошлом окне ноль —
 * рост «с нуля» в процентах не выражается и врал бы бесконечностью.
 */
export function periodDelta(
  period: StatisticsPeriod,
  days: DailyActivity[],
  today: Date = new Date(),
): PeriodDelta | null {
  const window = WINDOW_DAYS[period];
  if (window === undefined || days.length === 0) return null;

  const end = new Date(today);
  end.setHours(0, 0, 0, 0);

  const currentStart = addDays(end, -(window - 1));
  const previousStart = addDays(currentStart, -window);
  const previousEnd = addDays(currentStart, -1);

  const current = sumBetween(days, currentStart, end);
  const previous = sumBetween(days, previousStart, previousEnd);

  if (previous <= 0) return null;

  return {
    current,
    previous,
    percent: Math.round(((current - previous) / previous) * 100),
  };
}

function addDays(date: Date, days: number): Date {
  const shifted = new Date(date);
  shifted.setDate(shifted.getDate() + days);

  return shifted;
}

function sumBetween(days: DailyActivity[], from: Date, to: Date): number {
  let total = 0;

  for (const day of days) {
    const date = parseLocalDate(day.date);
    if (date >= from && date <= to) total += day.listenedSeconds;
  }

  return total;
}
