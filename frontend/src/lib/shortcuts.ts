// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { TranslationKey } from "@/lib/i18n";

type ShortcutAction =
  | "playPause"
  | "seekBy"
  | "seekPercent"
  | "next"
  | "previous"
  | "volumeBy"
  | "mute"
  | "favorite"
  | "shuffle"
  | "repeat"
  | "queue";

interface ShortcutHit {
  action: ShortcutAction;
  value?: number;
}

interface KeyLike {
  key: string;
  code?: string;
  shiftKey: boolean;
  altKey: boolean;
  ctrlKey: boolean;
  metaKey: boolean;
}

const ASCII_KEY = /^[a-z0-9]$/i;

function layoutSafeKey(event: KeyLike): string {
  if (ASCII_KEY.test(event.key)) return event.key.toLowerCase();

  const code = event.code ?? "";
  if (code.startsWith("Key")) return code.slice(3).toLowerCase();
  if (code.startsWith("Digit")) return code.slice(5);

  return event.key;
}

export const SEEK_STEP = 5;

export const NUDGE_STEP = 10;

export const SHORTCUT_VOLUME_STEP = 0.05;

const NEEDS_TRACK: ReadonlySet<ShortcutAction> = new Set<ShortcutAction>([
  "playPause",
  "seekBy",
  "seekPercent",
  "next",
  "previous",
  "favorite",
]);

export function shortcutNeedsTrack(action: ShortcutAction): boolean {
  return NEEDS_TRACK.has(action);
}

export function resolveShortcut(event: KeyLike): ShortcutHit | null {
  if (event.ctrlKey || event.metaKey || event.altKey) return null;

  if (event.key === "+" || event.key === "=") {
    return { action: "volumeBy", value: SHORTCUT_VOLUME_STEP };
  }
  if (event.key === "-" || event.key === "_") {
    return { action: "volumeBy", value: -SHORTCUT_VOLUME_STEP };
  }

  if (event.shiftKey) {
    if (event.key === "ArrowRight") return { action: "next" };
    if (event.key === "ArrowLeft") return { action: "previous" };
    return null;
  }

  const key = layoutSafeKey(event);

  if (key.length === 1 && key >= "0" && key <= "9") {
    return { action: "seekPercent", value: Number(key) * 10 };
  }

  switch (key) {
    case " ":
    case "k":
      return { action: "playPause" };
    case "ArrowRight":
      return { action: "seekBy", value: SEEK_STEP };
    case "ArrowLeft":
      return { action: "seekBy", value: -SEEK_STEP };
    case "l":
      return { action: "seekBy", value: NUDGE_STEP };
    case "j":
      return { action: "seekBy", value: -NUDGE_STEP };
    case "m":
      return { action: "mute" };
    case "f":
      return { action: "favorite" };
    case "s":
      return { action: "shuffle" };
    case "r":
      return { action: "repeat" };
    case "q":
      return { action: "queue" };
    default:
      return null;
  }
}

/**
 * `?` открывает справку по клавишам. По `code` тоже: в русской раскладке физическая клавиша
 * `?` с Shift печатает запятую, и по одному `key` справка там не открывалась бы.
 */
export function isHelpShortcut(event: KeyLike): boolean {
  if (event.ctrlKey || event.metaKey || event.altKey) return false;

  return event.key === "?" || (event.shiftKey && event.code === "Slash");
}

export function isTypingTarget(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;

  return (
    element?.tagName === "INPUT" ||
    element?.tagName === "TEXTAREA" ||
    element?.isContentEditable === true
  );
}

/**
 * Справка лежит рядом с `resolveShortcut`, а не в компоненте: новая клавиша, добавленная
 * там, но забытая здесь, сразу видна при правке одного файла.
 */
export const SHORTCUT_HELP: ReadonlyArray<{
  keys: readonly string[];
  label: TranslationKey;
  values?: Record<string, number>;
}> = [
  { keys: ["Space", "K"], label: "shortcuts.playPause" },
  { keys: ["←", "→"], label: "shortcuts.seek", values: { seconds: SEEK_STEP } },
  { keys: ["J", "L"], label: "shortcuts.seek", values: { seconds: NUDGE_STEP } },
  { keys: ["0–9"], label: "shortcuts.seekPercent" },
  { keys: ["Shift ←", "Shift →"], label: "shortcuts.track" },
  { keys: ["−", "+"], label: "shortcuts.volume" },
  { keys: ["M"], label: "shortcuts.mute" },
  { keys: ["F"], label: "shortcuts.favorite" },
  { keys: ["S"], label: "shortcuts.shuffle" },
  { keys: ["R"], label: "shortcuts.repeat" },
  { keys: ["Q"], label: "shortcuts.queue" },
  { keys: ["?"], label: "shortcuts.help" },
];
