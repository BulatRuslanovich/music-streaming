// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef } from "react";
import { recordEvent } from "@/lib/events";

// Показ полки — когда она хотя бы наполовину попала на экран; один раз за открытие главной.
// Вместе с источником запусков это даёт конверсию «показали → включили».
const VISIBLE_SHARE = 0.5;

export function useShelfImpression<T extends Element>(source: string) {
  const ref = useRef<T>(null);

  useEffect(() => {
    const element = ref.current;
    if (!element || typeof IntersectionObserver === "undefined") return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (!entries.some((entry) => entry.isIntersecting)) return;

        recordEvent({ type: "shelfShown", source });
        observer.disconnect();
      },
      { threshold: VISIBLE_SHARE },
    );

    observer.observe(element);
    return () => observer.disconnect();
  }, [source]);

  return ref;
}
