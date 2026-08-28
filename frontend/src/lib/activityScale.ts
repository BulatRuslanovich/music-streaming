// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { DailyActivity } from "@/lib/types";
import type { TranslationKey, TranslationValues } from "@/lib/i18n";

/**
 * Шкала графиков активности. Вынесена из `ActivityChart`, потому что теперь тех же делений
 * и тех же подписей просит не только столбчатый график: у длинных периодов это теплокарта,
 * а у суток — циферблат.
 */

const NICE_STEPS = [60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400];

const MAX_TICK_INTERVALS = 4;

/** Порог, за которым столбики становятся уже пикселя и график читать нечем. */
export const DENSE_FROM = 90;

export function scaleFor(max: number): { top: number; ticks: number[] } {
  if (max <= 0) return { top: 1, ticks: [0] };

  const step =
    NICE_STEPS.find((candidate) => max / candidate <= MAX_TICK_INTERVALS) ??
    NICE_STEPS[NICE_STEPS.length - 1];

  const top = Math.ceil(max / step) * step;
  const ticks: number[] = [];

  for (let value = 0; value <= top; value += step) ticks.push(value);

  return { top, ticks };
}

export function tickLabel(
  seconds: number,
  t: (key: TranslationKey, values?: TranslationValues) => string,
): string {
  if (seconds <= 0) return "0";
  if (seconds < 3600) return t("unit.minutes", { count: Math.round(seconds / 60) });

  const hours = Math.floor(seconds / 3600);
  const minutes = Math.round((seconds % 3600) / 60);

  return minutes === 0
    ? t("unit.hours", { count: hours })
    : t("unit.hoursMinutes", { hours, minutes });
}

export function heightOf(value: number, top: number): number {
  if (value <= 0 || top <= 0) return 0;
  return Math.min(100, (value / top) * 100);
}

/**
 * Пять ступеней насыщенности. Нулю соответствует ступень 0 — пустая клетка, её ни с чем
 * не спутать; всё остальное распределяется по корню, иначе один выходной с марафоном
 * утаскивает шкалу и вся остальная сетка становится одинаково бледной.
 */
export function intensityOf(value: number, max: number): 0 | 1 | 2 | 3 | 4 {
  if (value <= 0 || max <= 0) return 0;

  const share = Math.sqrt(value / max);

  if (share > 0.75) return 4;
  if (share > 0.5) return 3;
  if (share > 0.25) return 2;
  return 1;
}

/** `YYYY-MM-DD` как локальная дата: `new Date(iso)` разобрал бы её как UTC и сдвинул день. */
export function parseLocalDate(iso: string): Date {
  const [year, month, day] = iso.slice(0, 10).split("-").map(Number);
  return new Date(year, (month ?? 1) - 1, day ?? 1);
}

/** Понедельник — нулевой: календарь в обеих локалях проекта начинается с него. */
export function weekdayIndex(date: Date): number {
  return (date.getDay() + 6) % 7;
}

function isoDay(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");

  return `${date.getFullYear()}-${month}-${day}`;
}

/**
 * Достраивает пропущенные дни нулями. Сервер отдаёт `byDay` через `GROUP BY`, то есть
 * только дни, в которые что-то слушали, — и график из трёх толстых столбиков подряд
 * выдавал три активных дня за непрерывный месяц. Ось времени должна быть равномерной
 * независимо от того, слушали в этот день или нет.
 *
 * `from` — начало периода по данным сервера; для «всего времени» его нет, и тогда
 * отсчёт идёт от первого дня с активностью.
 */
export function densifyDays(days: DailyActivity[], from?: string | null): DailyActivity[] {
  if (days.length === 0) return days;

  const known = new Map(days.map((day) => [day.date.slice(0, 10), day]));

  const start = parseLocalDate(from ?? days[0].date);
  const lastKnown = parseLocalDate(days[days.length - 1].date);

  const today = new Date();
  today.setHours(0, 0, 0, 0);

  // Период считается в часовом поясе слушателя на сервере, а «сегодня» здесь — местное.
  // Расхождение стоит максимум одного пустого дня в хвосте, поэтому берём дальнюю границу.
  const end = lastKnown > today ? lastKnown : today;

  const filled: DailyActivity[] = [];

  for (const cursor = new Date(start); cursor <= end; cursor.setDate(cursor.getDate() + 1)) {
    const date = isoDay(cursor);
    filled.push(known.get(date) ?? { date, listenedSeconds: 0, plays: 0 });
  }

  return filled;
}
