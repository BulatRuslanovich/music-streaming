"use client";

import { useSyncExternalStore } from "react";

const neverChanges = () => () => {};

const isApplePlatform = () => /mac|iphone|ipad|ipod/i.test(navigator.userAgent);

export function useSearchShortcutLabel(): string {
  return useSyncExternalStore(neverChanges, isApplePlatform, () => false) ? "⌘K" : "Ctrl K";
}
