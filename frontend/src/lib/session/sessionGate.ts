// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/** Чем закончилась попытка обновить сессию перед серверным рендером. */
export type RenewalStatus = "renewed" | "rejected" | "unavailable";

export type SessionGate = "signedIn" | "signedOut" | "sessionEnded";

/**
 * Пускать ли к странице — решение proxy (бывший middleware), вынесенное сюда целиком.
 *
 * Оно стоило нам запертых слушателей, поэтому живёт отдельно и под тестами. Ловушка была в том,
 * что подсказка `ms_session` (не HttpOnly, живёт столько же, сколько refresh-токен) в одиночку
 * считалась доказательством входа. Она переживает отзыв токена — и слушатель с мёртвой сессией
 * попадал в петлю: клиент ловил 401, уходил на /login, proxy видел подсказку и заворачивал
 * его обратно на страницу, где всё снова отвечало 401. Разрывалось только инкогнито.
 *
 * `sessionEnded` отделено от `signedOut` намеренно: это единственный случай, когда нужно ещё и
 * унести мёртвые куки, иначе следующая навигация начнёт всё заново.
 *
 * Недоступность бэкенда — не повод разлогинивать: иначе его перезапуск выкидывал бы всех.
 */
export function sessionGate({
  renewal,
  hasRefreshCookie,
  hasSessionHint,
}: {
  renewal: RenewalStatus;
  hasRefreshCookie: boolean;
  hasSessionHint: boolean;
}): SessionGate {
  if (renewal === "renewed") return "signedIn";
  if (renewal === "rejected") return "sessionEnded";

  return hasRefreshCookie && hasSessionHint ? "signedIn" : "signedOut";
}
