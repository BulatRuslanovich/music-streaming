// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Тональность из анализа: 0 = C, дальше по полутонам. Диезы, а не бемоли, — анализатор
 * называет ступень, а не тональность нотной записи, и выбирать между F♯ и G♭ ему нечем.
 */
const PITCH_CLASSES = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"] as const;

export function pitchClass(key: number | null | undefined): string | null {
  if (key === null || key === undefined) return null;
  if (!Number.isInteger(key) || key < 0 || key >= PITCH_CLASSES.length) return null;

  return PITCH_CLASSES[key];
}

/**
 * Камелот — колесо гармонической сводки: соседние номера сочетаются, буква отличает лад.
 * A-мажор (key 9) — это 11B, отсюда сдвиг: мажор идёт с шагом в квинту от 8B на C.
 */
const CAMELOT_MAJOR = [8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1] as const;

export function camelot(key: number | null | undefined, isMinor: boolean): string | null {
  if (key === null || key === undefined) return null;
  if (!Number.isInteger(key) || key < 0 || key >= CAMELOT_MAJOR.length) return null;

  // Минор отстоит от одноимённого мажора на три позиции колеса назад: A-минор = 8A при C = 8B.
  const number = isMinor ? ((CAMELOT_MAJOR[key] + 8) % 12) + 1 : CAMELOT_MAJOR[key];

  return `${number}${isMinor ? "A" : "B"}`;
}
