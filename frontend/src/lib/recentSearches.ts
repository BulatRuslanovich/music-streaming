// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

const STORAGE_KEY = "music-streaming.recent-searches";
const LIMIT = 8;

/**
 * История запросов живёт только в браузере: серверу она не нужна, а на пустом экране поиска это
 * единственное, что можно показать осмысленного. Любая ошибка localStorage (приватный режим,
 * запрет на хранение) молча означает «истории нет».
 *
 * Отдаётся через `useSyncExternalStore`, а не через эффект: снапшот для сервера — пустой список,
 * поэтому гидрация не расходится, и никакого setState в эффекте не нужно.
 */
const EMPTY: string[] = [];

let cached: string[] | null = null;
const listeners = new Set<() => void>();

function read(): string[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return EMPTY;

    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return EMPTY;

    const values = parsed.filter((item): item is string => typeof item === "string");
    return values.length === 0 ? EMPTY : values.slice(0, LIMIT);
  } catch {
    return EMPTY;
  }
}

function publish(next: string[]): void {
  cached = next;
  listeners.forEach((listener) => listener());
}

export function subscribeToRecentSearches(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Ссылка на массив обязана быть стабильной между вызовами, иначе store зациклит рендер. */
export function getRecentSearches(): string[] {
  cached ??= read();
  return cached;
}

export function getServerRecentSearches(): string[] {
  return EMPTY;
}

export function rememberSearch(query: string): void {
  const value = query.trim();
  if (value.length === 0) return;

  const current = getRecentSearches();

  // Повтор запроса поднимает его наверх, а не плодит дубликаты.
  if (current[0] === value) return;

  const next = [value, ...current.filter((item) => item !== value)].slice(0, LIMIT);

  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    // Не сохранилось — не беда, экран всё равно отрисуется.
  }

  publish(next);
}

export function clearRecentSearches(): void {
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // см. выше
  }

  publish(EMPTY);
}
