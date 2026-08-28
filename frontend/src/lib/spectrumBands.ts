// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export const SPECTRUM_BANDS = 32;
export const MIN_HZ = 40;
export const MAX_HZ = 16_000;

/** Пределы анализатора. Из них считается цена одного байта в дБ для наклона спектра. */
export const MIN_DB = -80;
export const MAX_DB = -20;
export const BYTES_PER_DB = 255 / (MAX_DB - MIN_DB);

const NOISE_FLOOR_BYTES = 6;
const FULL_TILT_BYTES = 44;

/** Границы логарифмических полос в линейных бинах БПФ. */
export function bandEdges(sampleRate: number, binCount: number): number[] {
  const perBin = sampleRate / 2 / binCount;
  const edges: number[] = [];

  for (let band = 0; band <= SPECTRUM_BANDS; band += 1) {
    const hz = MIN_HZ * (MAX_HZ / MIN_HZ) ** (band / SPECTRUM_BANDS);
    edges.push(Math.min(binCount, Math.round(hz / perBin)));
  }

  // В нижних полосах шаг лога тоньше одного бина; без этого они схлопываются в пустые.
  for (let band = 1; band <= SPECTRUM_BANDS; band += 1) {
    if (edges[band] <= edges[band - 1]) {
      edges[band] = Math.min(binCount, edges[band - 1] + 1);
    }
  }

  return edges;
}

/** Компенсация естественного спада энергии музыки к верхним октавам, в байтах анализатора. */
export function bandTilt(dbPerOctave: number): Float32Array {
  const tilt = new Float32Array(SPECTRUM_BANDS);
  const octaves = Math.log2(MAX_HZ / MIN_HZ);

  for (let band = 0; band < SPECTRUM_BANDS; band += 1) {
    tilt[band] = ((band + 0.5) / SPECTRUM_BANDS) * octaves * dbPerOctave * BYTES_PER_DB;
  }

  return tilt;
}

/** Нормализует пик и плавно открывает наклон только над шумовым полом. */
export function bandLevel(peak: number, tilt: number): number {
  const over = peak - NOISE_FLOOR_BYTES;
  if (over <= 0) return 0;

  // Наклон растёт вместе с полезным сигналом: цифровая тишина не превращается в лестницу.
  const gate = Math.min(1, over / FULL_TILT_BYTES);
  return Math.min(1, (over + tilt * gate) / 255);
}
