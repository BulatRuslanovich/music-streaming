"use client";

/**
 * The page itself never scrolls — `body` is `overflow: hidden` and the `.content` column owns the
 * scrollbar (see styles/base.css and styles/shell.css). `window.scrollTo` is therefore a no-op.
 */
export function scrollContentToTop(): void {
  const content = document.querySelector<HTMLElement>("main.content");
  (content ?? document.scrollingElement ?? document.documentElement).scrollTo({ top: 0 });
}
