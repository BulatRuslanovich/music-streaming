// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useSyncExternalStore } from "react";

/**
 * Включён ли спектр. Настройка сознательно **не** уезжает в `UserSettings`: это свойство
 * устройства (его процессора), а не вкуса слушателя, и на сервере ему делать нечего —
 * там она стоила бы свойства опции, правила валидации, записи в `.env.example` и
 * маппинга в docker-compose. Тот же приём, что у сворачивания сайдбара и у переключения
 * оставшегося времени: localStorage плюс useSyncExternalStore.
 */

const KEY = "music-streaming.visualizer";

let enabled: boolean | null = null;

const listeners = new Set<() => void>();

function read(): boolean {
  try {
    // По умолчанию включён: выключатель нужен тем, кому он мешает, а не наоборот.
    return window.localStorage.getItem(KEY) !== "off";
  } catch {
    return true;
  }
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function snapshot(): boolean {
  enabled ??= read();
  return enabled;
}

function serverSnapshot(): boolean {
  return false;
}

export function setVisualizerEnabled(next: boolean): void {
  enabled = next;

  try {
    window.localStorage.setItem(KEY, next ? "on" : "off");
  } catch {}

  listeners.forEach((listener) => listener());
}

export function useVisualizerEnabled(): boolean {
  return useSyncExternalStore(subscribe, snapshot, serverSnapshot);
}
