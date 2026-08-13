"use client";

import { useRouter } from "next/navigation";
import { useEffect, useSyncExternalStore } from "react";

const neverChanges = () => () => {};

const isApplePlatform = () => /mac|iphone|ipad|ipod/i.test(navigator.userAgent);

export function useSearchShortcut() {
  const router = useRouter();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.code !== "KeyK" || !(event.metaKey || event.ctrlKey)) return;

      event.preventDefault();
      router.push("/search");
    };

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [router]);
}

export function useSearchShortcutLabel(): string {
  return useSyncExternalStore(neverChanges, isApplePlatform, () => false) ? "⌘K" : "Ctrl K";
}
