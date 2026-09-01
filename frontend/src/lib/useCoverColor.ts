// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useSyncExternalStore } from "react";

interface CoverSample {
  tint: string | null;

  /** Второй по весу оттенок обложки — второй полюс для фоновой подложки. */
  tintAlt: string | null;
}

const EMPTY: CoverSample = { tint: null, tintAlt: null };

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

export function useCoverColor(source: string | null): string | null {
  return useSample(source).tint;
}

/** Оба полюса обложки сразу — нужно только фоновой подложке. */
export function useCoverPalette(source: string | null): CoverSample {
  return useSample(source);
}

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

  const [tint, tintAlt] = dominantColors(pixels);

  return { tint, tintAlt };
}

/** Бакеты ближе этого расстояния — по сути один цвет, второй полюс из них не выйдет. */
const MIN_BUCKET_DISTANCE = 2;

/** Развод оттенков, когда обложка одноцветная и второго полюса в ней просто нет. */
const FALLBACK_HUE_SHIFT = 38;

function dominantColors(pixels: Uint8ClampedArray): [string | null, string | null] {
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

  if (weights[best] <= 0) return [null, null];

  // Второй полюс ищем поодаль от первого — соседний бакет дал бы тот же цвет.
  let second = -1;
  for (let bucket = 0; bucket < HUE_BUCKETS; bucket += 1) {
    if (weights[bucket] <= 0) continue;
    if (bucketDistance(bucket, best) < MIN_BUCKET_DISTANCE) continue;
    if (second === -1 || weights[bucket] > weights[second]) second = bucket;
  }

  const tint = colorOf(best, hues, weights, saturations);

  if (second === -1) {
    const hue = Math.round(hues[best] / weights[best]);
    const saturation = saturations[best] / weights[best];

    return [tint, hslOf(hue + FALLBACK_HUE_SHIFT, saturation)];
  }

  return [tint, colorOf(second, hues, weights, saturations)];
}

function bucketDistance(left: number, right: number): number {
  const raw = Math.abs(left - right);
  return Math.min(raw, HUE_BUCKETS - raw);
}

function colorOf(bucket: number, hues: number[], weights: number[], saturations: number[]): string {
  return hslOf(Math.round(hues[bucket] / weights[bucket]), saturations[bucket] / weights[bucket]);
}

function hslOf(hue: number, saturation: number): string {
  const clamped = Math.round(Math.min(0.85, Math.max(0.35, saturation)) * 100);

  return `hsl(${((hue % 360) + 360) % 360} ${clamped}% 32%)`;
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
