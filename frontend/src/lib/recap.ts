// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export type Daypart = "morning" | "day" | "evening" | "night";

// Те же границы, что у дневных миксов на сервере: утро 5–11, день 11–17, вечер 17–23, ночь 23–5.
export function daypartOf(hour: number): Daypart {
  if (hour >= 5 && hour < 11) return "morning";
  if (hour >= 11 && hour < 17) return "day";
  if (hour >= 17 && hour < 23) return "evening";
  return "night";
}

export function dominantDaypart(hourSeconds: number[]): Daypart | null {
  const totals: Record<Daypart, number> = { morning: 0, day: 0, evening: 0, night: 0 };
  hourSeconds.forEach((seconds, hour) => (totals[daypartOf(hour)] += seconds));

  const [best, seconds] = Object.entries(totals).sort((a, b) => b[1] - a[1])[0];
  return seconds > 0 ? (best as Daypart) : null;
}

export function busiestDay(daySeconds: number[]): { day: number; seconds: number } | null {
  let best: { day: number; seconds: number } | null = null;

  daySeconds.forEach((seconds, index) => {
    if (seconds > 0 && (!best || seconds > best.seconds)) best = { day: index + 1, seconds };
  });

  return best;
}

// Сколько пустых клеток перед первым числом в неделе, начинающейся с понедельника.
export function leadingBlanks(year: number, month: number): number {
  return (new Date(year, month - 1, 1).getDay() + 6) % 7;
}

// Корень растягивает тихие дни: при линейной шкале один марафон гасит весь остальной месяц.
export function intensity(seconds: number, max: number): number {
  return max > 0 && seconds > 0 ? Math.sqrt(seconds / max) : 0;
}

export function monthKey(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, "0")}`;
}

export function parseMonthKey(key: string | null): { year: number; month: number } | null {
  const match = key?.match(/^(\d{4})-(\d{2})$/);
  if (!match) return null;

  const month = Number(match[2]);
  return month >= 1 && month <= 12 ? { year: Number(match[1]), month } : null;
}
