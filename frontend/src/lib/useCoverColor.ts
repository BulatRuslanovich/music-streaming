// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useSyncExternalStore } from "react";

const cache = new Map<string, string | null>();

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

export function useCoverColor(source: string | null): string | null {
  const color = useSyncExternalStore(
    subscribe,
    () => (source ? (cache.get(source) ?? null) : null),
    () => null,
  );

  useEffect(() => {
    if (!source || cache.has(source) || pending.has(source)) return;

    pending.add(source);
    const image = new Image();
    image.decoding = "async";

    const settle = (result: string | null) => {
      cache.set(source, result);
      pending.delete(source);
      for (const listener of listeners) listener();
    };

    image.onload = () => settle(dominantColor(image));
    image.onerror = () => settle(null);

    image.src = source;
  }, [source]);

  return color;
}

function dominantColor(image: HTMLImageElement): string | null {
  const canvas = document.createElement("canvas");
  canvas.width = SAMPLE_SIZE;
  canvas.height = SAMPLE_SIZE;

  const context = canvas.getContext("2d", { willReadFrequently: true });
  if (!context) return null;

  context.drawImage(image, 0, 0, SAMPLE_SIZE, SAMPLE_SIZE);

  let pixels: Uint8ClampedArray;
  try {
    pixels = context.getImageData(0, 0, SAMPLE_SIZE, SAMPLE_SIZE).data;
  } catch {
    return null;
  }

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

  const clamped = Math.round(Math.min(0.6, Math.max(0.28, saturation)) * 100);

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
