// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

const STORAGE_KEY = "music-streaming.recent-searches";
const LIMIT = 8;

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

  if (current[0] === value) return;

  const next = [value, ...current.filter((item) => item !== value)].slice(0, LIMIT);

  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {}

  publish(next);
}

export function clearRecentSearches(): void {
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {}

  publish(EMPTY);
}
