// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect } from "react";

export function useCoverAccent(tint: string | null, tintAlt: string | null = null): void {
  useEffect(() => {
    const root = document.documentElement;

    if (tint) root.style.setProperty("--cover-tint", tint);
    else root.style.removeProperty("--cover-tint");

    // Второй полюс держим осмысленным даже у одноцветной обложки: без него
    // подложка вырождается в одно пятно.
    if (tintAlt ?? tint) root.style.setProperty("--cover-tint-2", (tintAlt ?? tint)!);
    else root.style.removeProperty("--cover-tint-2");
  }, [tint, tintAlt]);
}
