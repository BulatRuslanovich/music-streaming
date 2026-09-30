// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Web Storage, который не бросает. В приватном режиме, при запрете сайтам хранить данные или
 * переполнении квоты бросает не только `setItem`, но и `getItem`, и даже сам доступ к
 * `window.localStorage`. Раньше каждое место заворачивало это в свой try/catch — а где-то
 * забывало (идентификатор устройства) или бросало снова изнутри catch (`removeItem` после
 * неудачного чтения). Здесь все отказы гасятся одинаково: чтение даёт null, запись — ничего.
 */

type StorageArea = "local" | "session";

function areaOf(kind: StorageArea): Storage | null {
  try {
    return kind === "session" ? window.sessionStorage : window.localStorage;
  } catch {
    return null;
  }
}

export function readStored(key: string, kind: StorageArea = "local"): string | null {
  try {
    return areaOf(kind)?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

/** `null` удаляет ключ. */
export function writeStored(key: string, value: string | null, kind: StorageArea = "local"): void {
  try {
    const area = areaOf(kind);
    if (value === null) area?.removeItem(key);
    else area?.setItem(key, value);
  } catch {}
}

/**
 * Разобранный JSON или null. Испорченную запись заодно удаляет, чтобы не спотыкаться о неё
 * на каждом запуске. Форму значения проверяет вызывающий: здесь известно только, что это JSON.
 */
export function readStoredJson(key: string, kind: StorageArea = "local"): unknown {
  const raw = readStored(key, kind);
  if (raw === null) return null;

  try {
    return JSON.parse(raw) as unknown;
  } catch {
    writeStored(key, null, kind);
    return null;
  }
}

export function writeStoredJson(key: string, value: unknown, kind: StorageArea = "local"): void {
  let serialized: string;
  try {
    serialized = JSON.stringify(value);
  } catch {
    return;
  }

  writeStored(key, serialized, kind);
}
