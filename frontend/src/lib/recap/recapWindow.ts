// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Окно итогов месяца: первые семь дней нового месяца по часам слушателя.
 *
 * Решение принимается на клиенте, потому что вне окна фичи не существует целиком — нет ни
 * запроса, ни пункта меню, ни баннера. Спрашивать об этом сервер значило бы ходить за ответом
 * «ничего нет» двадцать с лишним дней подряд. Сервер проверяет то же самое независимо, так что
 * клиентская проверка — про интерфейс, а не про доступ.
 */

export const RECAP_WINDOW_DAYS = 7;

export interface RecapWindow {
  open: boolean;
  /** Месяц итогов — всегда предыдущий, в формате `YYYY-MM`. */
  month: string;
}

export function recapWindow(now: Date, timeZone: string): RecapWindow {
  const [year, month, day] = localParts(now, timeZone);
  const previousYear = month === 1 ? year - 1 : year;
  const previousMonth = month === 1 ? 12 : month - 1;

  return {
    open: day <= RECAP_WINDOW_DAYS,
    month: `${previousYear}-${String(previousMonth).padStart(2, "0")}`,
  };
}

/** Год, месяц и день в поясе слушателя: `en-CA` отдаёт готовый `YYYY-MM-DD`. */
function localParts(now: Date, timeZone: string): [number, number, number] {
  try {
    const [year, month, day] = new Intl.DateTimeFormat("en-CA", {
      timeZone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    })
      .format(now)
      .split("-")
      .map(Number);

    if (year && month && day) return [year, month, day];
  } catch {
    // Пояс приходит из настроек, где его проверял Postgres, — Intl может знать не тот же список.
  }

  return [now.getFullYear(), now.getMonth() + 1, now.getDate()];
}
