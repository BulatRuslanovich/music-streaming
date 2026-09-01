// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useState, type RefObject } from "react";

/**
 * Края горизонтальной ленты: доехали ли до начала и до конца. Нужно только стрелкам, а они
 * `max-md:hidden` — на телефоне вся эта работа впустую, поэтому и наблюдатель один на всё дерево,
 * а не по одному на полку. На главной их разом до семи.
 */
const callbacks = new WeakMap<Element, () => void>();

let observer: ResizeObserver | null = null;

function shared(): ResizeObserver | null {
  if (typeof ResizeObserver === "undefined") return null;

  observer ??= new ResizeObserver((entries) => {
    for (const entry of entries) callbacks.get(entry.target)?.();
  });

  return observer;
}

interface ShelfEdges {
  atStart: boolean;
  atEnd: boolean;
  scrollShelf: (direction: 1 | -1) => void;
}

export function useShelfEdges(ref: RefObject<HTMLDivElement | null>): ShelfEdges {
  const [atStart, setAtStart] = useState(true);
  const [atEnd, setAtEnd] = useState(true);

  useEffect(() => {
    const element = ref.current;
    if (!element) return;

    const update = () => {
      const furthest = element.scrollWidth - element.clientWidth;
      setAtStart(element.scrollLeft <= 1);
      setAtEnd(element.scrollLeft >= furthest - 1);
    };

    element.addEventListener("scroll", update, { passive: true });

    const resize = shared();
    callbacks.set(element, update);
    resize?.observe(element);

    update();

    return () => {
      element.removeEventListener("scroll", update);
      resize?.unobserve(element);
      callbacks.delete(element);
    };
  }, [ref]);

  const scrollShelf = useCallback(
    (direction: 1 | -1) => {
      const element = ref.current;
      if (!element) return;
      element.scrollBy({ left: direction * element.clientWidth * 0.8, behavior: "smooth" });
    },
    [ref],
  );

  return { atStart, atEnd, scrollShelf };
}
