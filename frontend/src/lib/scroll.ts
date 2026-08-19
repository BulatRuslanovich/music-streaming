// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

export function scrollContentToTop(): void {
  const content = document.querySelector<HTMLElement>("main.content");
  (content ?? document.scrollingElement ?? document.documentElement).scrollTo({ top: 0 });
}
