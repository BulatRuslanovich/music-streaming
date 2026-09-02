// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

/** 4′33″ — ровно столько длится пьеса Кейджа, и ровно столько нужно ничего не делать. */
export const CAGE_MS = (4 * 60 + 33) * 1000;

/**
 * Исполнение помнится в пределах сессии, а не браузера.
 *
 * Отметка «один раз и навсегда» жила в localStorage и ставилась в момент, когда отсчёт
 * дошёл до конца, — а увидеть строку можно только на полноэкранном плеере. Тот, кто отошёл
 * от компьютера и вернулся к обычной панели, тратил свой единственный шанс, ничего не увидев.
 * В пределах сессии эта дыра закрывается сама: находка дождётся, пока откроют арт, а новая
 * сессия просто заводит отсчёт заново — тишину той же длины всё равно надо выдержать ещё раз.
 */
const listeners = new Set<() => void>();

let performed = false;

export function subscribeCage(listener: () => void): () => void {
  listeners.add(listener);

  return () => {
    listeners.delete(listener);
  };
}

export function cagePerformed(): boolean {
  return performed;
}

/** Снимок для сервера: до гидратации исполнить пьесу негде. */
export function serverCagePerformed(): boolean {
  return false;
}

export function markCagePerformed(): void {
  if (performed) return;

  performed = true;
  listeners.forEach((listener) => listener());
}
