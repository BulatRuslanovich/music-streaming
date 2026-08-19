import type { TranslationKey } from "@/lib/i18n";

export type ShortcutAction =
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
  | "queue"
  | "palette"
  | "help";

export interface ShortcutHit {
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

export function layoutSafeKey(event: KeyLike): string {
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
  if (event.ctrlKey || event.metaKey) {
    return layoutSafeKey(event) === "k" ? { action: "palette" } : null;
  }

  if (event.altKey) return null;

  if (event.key === "?") return { action: "help" };
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

export interface ShortcutHelpGroup {
  titleKey: TranslationKey;
  items: { keys: string[]; labelKey: TranslationKey }[];
}

export function shortcutHelp(commandKey: string): ShortcutHelpGroup[] {
  return [
    {
      titleKey: "shortcuts.playback",
      items: [
        { keys: ["Space", "K"], labelKey: "shortcuts.playPause" },
        { keys: ["←", "→"], labelKey: "shortcuts.seek" },
        { keys: ["J", "L"], labelKey: "shortcuts.nudge" },
        { keys: ["Shift ←", "Shift →"], labelKey: "shortcuts.step" },
        { keys: ["0 … 9"], labelKey: "shortcuts.seekPercent" },
        { keys: ["+", "−"], labelKey: "shortcuts.volume" },
        { keys: ["M"], labelKey: "shortcuts.mute" },
      ],
    },
    {
      titleKey: "shortcuts.library",
      items: [
        { keys: ["F"], labelKey: "shortcuts.favorite" },
        { keys: ["S"], labelKey: "shortcuts.shuffle" },
        { keys: ["R"], labelKey: "shortcuts.repeat" },
        { keys: ["Q"], labelKey: "shortcuts.queue" },
        { keys: [commandKey], labelKey: "shortcuts.palette" },
        { keys: ["?"], labelKey: "shortcuts.help" },
      ],
    },
  ];
}
