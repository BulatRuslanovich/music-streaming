// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useSyncExternalStore, type CSSProperties } from "react";
import { coverTone, TONE_SAMPLE_EDGE, type CoverTone } from "./coverTone";

// undefined — ещё считается, null — обложки нет или она чёрно-белая.
const tones = new Map<string, CoverTone | null>();
const pending = new Map<string, Promise<void>>();
const listeners = new Set<() => void>();

const MAX_CACHED = 400;

function remember(url: string, tone: CoverTone | null): void {
  if (tones.size >= MAX_CACHED) tones.delete(tones.keys().next().value!);
  tones.set(url, tone);
  listeners.forEach((listener) => listener());
}

function sample(url: string): Promise<void> {
  const image = new Image();
  image.decoding = "async";
  image.src = url;

  return image
    .decode()
    .then(() => {
      const canvas = document.createElement("canvas");
      canvas.width = canvas.height = TONE_SAMPLE_EDGE;
      const context = canvas.getContext("2d", { willReadFrequently: true });
      if (!context) return remember(url, null);

      context.drawImage(image, 0, 0, TONE_SAMPLE_EDGE, TONE_SAMPLE_EDGE);
      const { data } = context.getImageData(0, 0, TONE_SAMPLE_EDGE, TONE_SAMPLE_EDGE);
      remember(url, coverTone(data));
    })
    .catch(() => remember(url, null))
    .finally(() => pending.delete(url));
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useCoverTone(url: string | null | undefined): CoverTone | null | undefined {
  const tone = useSyncExternalStore(
    subscribe,
    () => (url ? tones.get(url) : null),
    () => undefined,
  );

  if (url && tone === undefined && !pending.has(url) && typeof window !== "undefined") {
    pending.set(url, sample(url));
  }

  return tone;
}

// Переменные для элемента с data-tone: из них theme.css собирает --tone. Нейтральная обложка
// гасит тон через --tone-a, оттенок и насыщенность остаются прежними — так переход мягче.
export function toneStyle(tone: CoverTone | null | undefined): CSSProperties {
  if (!tone) return { ["--tone-a" as string]: 0 };

  return {
    ["--tone-h" as string]: tone.hue,
    ["--tone-c" as string]: tone.chroma,
    ["--tone-a" as string]: 1,
  };
}
