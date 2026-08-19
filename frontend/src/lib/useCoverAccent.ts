// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect } from "react";

export function useCoverAccent(tint: string | null): void {
  useEffect(() => {
    const root = document.documentElement;

    if (tint) root.style.setProperty("--cover-tint", tint);
    else root.style.removeProperty("--cover-tint");
  }, [tint]);
}
