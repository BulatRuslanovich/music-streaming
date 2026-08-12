"use client";

/**
 * Сама страница никогда не прокручивается: у `body` стоит `overflow: hidden`, а полосой прокрутки
 * владеет колонка `.content` (см. styles/base.css и styles/shell.css). Поэтому `window.scrollTo`
 * ничего не делает.
 */
export function scrollContentToTop(): void {
  const content = document.querySelector<HTMLElement>("main.content");
  (content ?? document.scrollingElement ?? document.documentElement).scrollTo({ top: 0 });
}
