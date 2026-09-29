// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

/** Прокручивает `<main>` оболочки: страница не скроллит окно, у неё свой контейнер. */
export function scrollContentToTop(): void {
  const content = document.querySelector<HTMLElement>("main");
  (content ?? document.scrollingElement ?? document.documentElement).scrollTo({ top: 0 });
}

/** Плавная прокрутка, если слушатель не просил убрать движение. */
export function smoothUnlessReduced(): ScrollBehavior {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth";
}
