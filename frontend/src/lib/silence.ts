// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

/** 4′33″ — ровно столько длится пьеса Кейджа, и ровно столько нужно ничего не делать. */
export const CAGE_MS = (4 * 60 + 33) * 1000;

const STORAGE_KEY = "ms_cage";

/**
 * Три состояния вместо флага: «ещё не исполнено» и «исполнено когда-то раньше» ведут себя
 * одинаково на экране, но по-разному в отсчёте — второе его вообще не заводит.
 */
export type CageState = "armed" | "performed" | "spent";

const listeners = new Set<() => void>();

let performedNow = false;

let ever: boolean | null = null;

function everPerformed(): boolean {
  if (ever === null) {
    try {
      ever = window.localStorage.getItem(STORAGE_KEY) !== null;
    } catch {
      ever = false;
    }
  }

  return ever;
}

export function subscribeCage(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function cageState(): CageState {
  if (performedNow) return "performed";

  return everPerformed() ? "spent" : "armed";
}

/**
 * Снимок для сервера. «armed» и «spent» рисуют одно и то же — ничего, — поэтому разметка
 * сервера и первого клиентского рендера совпадает независимо от того, что лежит в хранилище.
 */
export function serverCageState(): CageState {
  return "armed";
}

export function markCagePerformed(): void {
  if (performedNow) return;

  performedNow = true;
  ever = true;

  try {
    window.localStorage.setItem(STORAGE_KEY, new Date().toISOString());
  } catch {}

  listeners.forEach((listener) => listener());
}
