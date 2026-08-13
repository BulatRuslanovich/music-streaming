"use client";

import { useRouter } from "next/navigation";
import { useEffect, useSyncExternalStore } from "react";

/** Платформа не меняется за время жизни страницы, поэтому подписываться не на что. */
const neverChanges = () => () => {};

const isApplePlatform = () => /mac|iphone|ipad|ipod/i.test(navigator.userAgent);

/**
 * Открывает поиск по Ctrl+K (⌘K на Apple).
 *
 * Живёт в оболочке приложения, а не в самой ссылке: сочетание должно работать на любой странице,
 * в том числе когда до бокового меню не дотянуться.
 */
export function useSearchShortcut() {
  const router = useRouter();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      // По коду клавиши, а не по символу: на нелатинской раскладке event.key вернёт «л».
      if (event.code !== "KeyK" || !(event.metaKey || event.ctrlKey)) return;

      // Иначе браузер перехватит сочетание и уведёт фокус в адресную строку.
      event.preventDefault();
      router.push("/search");
    };

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [router]);
}

/**
 * Подпись сочетания для подсказки.
 *
 * Читается из браузера после гидратации: на сервере navigator недоступен, а разошедшаяся подпись
 * сломала бы гидратацию, поэтому серверный снимок — всегда вариант не для Apple.
 */
export function useSearchShortcutLabel(): string {
  return useSyncExternalStore(neverChanges, isApplePlatform, () => false) ? "⌘K" : "Ctrl K";
}
