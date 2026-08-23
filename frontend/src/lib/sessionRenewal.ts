// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Когда продлевать сессию, если её никто не трогает.
 *
 * `send()` из lib/http продлевает токен только в ответ на 401, но во время непрерывного
 * воспроизведения запросов к API нет вообще: очередь запросов отдаёт кэш, а звук аудиоэлемент
 * тянет сам, мимо обёртки. Токен тихо истекает посреди трека, и браузер сообщает об этом
 * как об ошибке источника, а не как об истёкшей сессии.
 */

/** Доля времени жизни токена, после которой его пора обновлять. */
const RENEW_AT = 2 / 3;

/** Ниже этого продлеваться чаще, чем раз в полминуты, смысла нет. */
const MINIMUM_MS = 30_000;

/** Страховка на случай, если сервер пришлёт что-то неправдоподобное. */
const MAXIMUM_MS = 60 * 60_000;

export function renewalIntervalMs(accessTokenMinutes: number): number {
  if (!Number.isFinite(accessTokenMinutes) || accessTokenMinutes <= 0) return MINIMUM_MS;

  const lifetime = accessTokenMinutes * 60_000;

  return Math.min(Math.max(lifetime * RENEW_AT, MINIMUM_MS), MAXIMUM_MS);
}

/**
 * Пора ли продлевать после возвращения вкладки из фона. Таймеры в фоновых вкладках душатся,
 * поэтому на `visibilitychange` расписание проверяется отдельно, по часам.
 */
export function isStale(lastRenewedAt: number, now: number, intervalMs: number): boolean {
  return now - lastRenewedAt >= intervalMs;
}
