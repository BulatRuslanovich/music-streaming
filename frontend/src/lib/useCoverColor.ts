// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useSyncExternalStore } from "react";

interface CoverSample {
  /** Доминирующий оттенок обложки — им красится акцент интерфейса. */
  tint: string | null;

  /** Светлый ли центр обложки. По нему выбирается цвет кнопок, лежащих поверх неё. */
  centerIsLight: boolean;
}

const EMPTY: CoverSample = { tint: null, centerIsLight: false };

const cache = new Map<string, CoverSample>();

const pending = new Set<string>();

const listeners = new Set<() => void>();

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

const SAMPLE_SIZE = 24;

const HUE_BUCKETS = 12;

function useSample(source: string | null): CoverSample {
  const sample = useSyncExternalStore(
    subscribe,
    () => (source ? (cache.get(source) ?? EMPTY) : EMPTY),
    () => EMPTY,
  );

  useEffect(() => {
    if (!source || cache.has(source) || pending.has(source)) return;

    pending.add(source);
    const image = new Image();
    image.decoding = "async";

    const settle = (result: CoverSample) => {
      cache.set(source, result);
      pending.delete(source);
      for (const listener of listeners) listener();
    };

    image.onload = () => settle(analyse(image));
    image.onerror = () => settle(EMPTY);

    image.src = source;
  }, [source]);

  return sample;
}

/** Доминирующий оттенок обложки. */
export function useCoverColor(source: string | null): string | null {
  return useSample(source).tint;
}

/**
 * Светлый ли центр обложки. Нужно тому, что рисуется поверх неё: на светлой картинке белые
 * значки пропадают, и их приходится делать тёмными.
 */
export function useCoverIsLight(source: string | null): boolean {
  return useSample(source).centerIsLight;
}

// Белый значок перестаёт читаться, когда под ним ярче этого порога: выше 0.3 его контраст с
// подложкой падает ниже 3:1. Ровно там и надо переключаться на тёмный.
const WHITE_FAILS_ABOVE = 0.3;

function analyse(image: HTMLImageElement): CoverSample {
  const canvas = document.createElement("canvas");
  canvas.width = SAMPLE_SIZE;
  canvas.height = SAMPLE_SIZE;

  const context = canvas.getContext("2d", { willReadFrequently: true });
  if (!context) return EMPTY;

  context.drawImage(image, 0, 0, SAMPLE_SIZE, SAMPLE_SIZE);

  let pixels: Uint8ClampedArray;
  try {
    pixels = context.getImageData(0, 0, SAMPLE_SIZE, SAMPLE_SIZE).data;
  } catch {
    return EMPTY;
  }

  return { tint: dominantColor(pixels), centerIsLight: centerIsLight(pixels) };
}

/** Средняя яркость полосы, по которой идёт ряд кнопок. Углы обложки на их читаемость не влияют. */
function centerIsLight(pixels: Uint8ClampedArray): boolean {
  const from = Math.floor(SAMPLE_SIZE / 3);
  const to = SAMPLE_SIZE - from;

  let total = 0;
  let counted = 0;

  for (let y = from; y < to; y += 1) {
    for (let x = 0; x < SAMPLE_SIZE; x += 1) {
      const index = (y * SAMPLE_SIZE + x) * 4;
      if (pixels[index + 3] < 128) continue;

      total += luminance(pixels[index], pixels[index + 1], pixels[index + 2]);
      counted += 1;
    }
  }

  return counted > 0 && total / counted > WHITE_FAILS_ABOVE;
}

function luminance(red: number, green: number, blue: number): number {
  const channel = (value: number) => {
    const c = value / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };

  return 0.2126 * channel(red) + 0.7152 * channel(green) + 0.0722 * channel(blue);
}

function dominantColor(pixels: Uint8ClampedArray): string | null {
  const weights = new Array<number>(HUE_BUCKETS).fill(0);
  const hues = new Array<number>(HUE_BUCKETS).fill(0);
  const saturations = new Array<number>(HUE_BUCKETS).fill(0);

  for (let index = 0; index < pixels.length; index += 4) {
    if (pixels[index + 3] < 128) continue;

    const [hue, saturation, lightness] = toHsl(pixels[index], pixels[index + 1], pixels[index + 2]);
    if (lightness < 0.12 || lightness > 0.9 || saturation < 0.1) continue;

    const bucket = Math.min(HUE_BUCKETS - 1, Math.floor((hue / 360) * HUE_BUCKETS));
    weights[bucket] += saturation;
    hues[bucket] += hue * saturation;
    saturations[bucket] += saturation * saturation;
  }

  let best = 0;
  for (let bucket = 1; bucket < HUE_BUCKETS; bucket += 1) {
    if (weights[bucket] > weights[best]) best = bucket;
  }

  if (weights[best] <= 0) return null;

  const hue = Math.round(hues[best] / weights[best]);
  const saturation = saturations[best] / weights[best];

  // Из этого цвета дальше берут только тон и насыщенность: светлоту `theme.css` всё равно задаёт
  // сам. Поэтому важен именно диапазон насыщенности — зажатый, он делал сочную обложку неотличимой
  // от блёклой. Нижняя граница остаётся, чтобы почти серая обложка не давала грязный акцент.
  const clamped = Math.round(Math.min(0.85, Math.max(0.35, saturation)) * 100);

  return `hsl(${hue} ${clamped}% 32%)`;
}

function toHsl(red: number, green: number, blue: number): [number, number, number] {
  const r = red / 255;
  const g = green / 255;
  const b = blue / 255;

  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const delta = max - min;
  const lightness = (max + min) / 2;

  if (delta === 0) return [0, 0, lightness];

  const saturation = delta / (1 - Math.abs(2 * lightness - 1));

  let hue: number;
  if (max === r) hue = ((g - b) / delta) % 6;
  else if (max === g) hue = (b - r) / delta + 2;
  else hue = (r - g) / delta + 4;

  hue *= 60;
  if (hue < 0) hue += 360;

  return [hue, saturation, lightness];
}
