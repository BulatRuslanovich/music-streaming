// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

// Полосы спектра для «живой» пластинки. Частоты слух воспринимает логарифмически, поэтому
// полосы нарезаются равными по октавам, а не по герцам: иначе половина лучей ушла бы на верха.
export function bandBins(
  bands: number,
  sampleRate: number,
  fftSize: number,
  lowHz = 40,
  highHz = 12_000,
): [number, number][] {
  const binHz = sampleRate / fftSize;
  const lastBin = fftSize / 2 - 1;
  const ratio = (highHz / lowHz) ** (1 / bands);
  const result: [number, number][] = [];

  for (let band = 0; band < bands; band++) {
    const from = Math.min(lastBin, Math.floor((lowHz * ratio ** band) / binHz));
    const to = Math.min(
      lastBin,
      Math.max(from + 1, Math.floor((lowHz * ratio ** (band + 1)) / binHz)),
    );
    result.push([from, to]);
  }

  return result;
}

// Громкость полосы в [0, 1] с подтянутым контрастом: тихие полосы почти гаснут, громкие — играют.
export function bandLevel(data: Uint8Array, [from, to]: [number, number]): number {
  let sum = 0;
  for (let bin = from; bin < to; bin++) sum += data[bin];

  const level = sum / (to - from) / 255;
  return level ** 1.6;
}

// Огибающая: быстро вверх, медленно вниз — лучи вздрагивают на ударе и плавно опадают.
export function follow(current: number, target: number, attack = 0.55, release = 0.12): number {
  return current + (target - current) * (target > current ? attack : release);
}

// Без анализатора (iOS, Android) пульс берётся из темпа трека: вспышка на каждой доле, затухание к следующей.
export function beatPulse(positionSeconds: number, bpm: number, offsetSeconds = 0): number {
  if (bpm <= 0) return 0;

  const beats = ((positionSeconds - offsetSeconds) * bpm) / 60;
  const phase = beats - Math.floor(beats);
  return Math.exp(-phase * 5);
}

// Удар баса: насколько текущий бас выше своего недавнего среднего.
export function onset(bass: number, average: number): number {
  return Math.max(0, Math.min(1, (bass - average * 1.08) * 4));
}
