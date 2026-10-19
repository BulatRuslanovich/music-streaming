// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export interface Spot {
  x: number;
  y: number;
}

// Вид карты: точка (x, y) из квадрата [0, 1] стоит на экране в (x·base·k + panX, y·base·k + panY),
// где base — меньшая сторона холста.
export interface MapView {
  k: number;
  panX: number;
  panY: number;
}

export const MIN_ZOOM = 0.8;
export const MAX_ZOOM = 40;

// Больше треков за раз не проигрывается: столько отдаёт /tracks/lookup.
export const MAX_SELECTION_PLAY = 300;

export function fitView(width: number, height: number): MapView {
  const base = Math.min(width, height);
  return { k: 1, panX: (width - base) / 2, panY: (height - base) / 2 };
}

export function toScreen(spot: Spot, view: MapView, base: number): Spot {
  return { x: spot.x * base * view.k + view.panX, y: spot.y * base * view.k + view.panY };
}

export function toMap(screen: Spot, view: MapView, base: number): Spot {
  return {
    x: (screen.x - view.panX) / (base * view.k),
    y: (screen.y - view.panY) / (base * view.k),
  };
}

// Масштаб вокруг точки экрана: то, что было под курсором, под ним и остаётся.
export function zoomAt(view: MapView, factor: number, at: Spot): MapView {
  const k = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, view.k * factor));
  const applied = k / view.k;

  return {
    k,
    panX: at.x - (at.x - view.panX) * applied,
    panY: at.y - (at.y - view.panY) * applied,
  };
}

export function centreOn(spot: Spot, k: number, width: number, height: number): MapView {
  const base = Math.min(width, height);
  return { k, panX: width / 2 - spot.x * base * k, panY: height / 2 - spot.y * base * k };
}

export function pointInPolygon(spot: Spot, polygon: Spot[]): boolean {
  let inside = false;

  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
    const a = polygon[i];
    const b = polygon[j];

    if (
      a.y > spot.y !== b.y > spot.y &&
      spot.x < ((b.x - a.x) * (spot.y - a.y)) / (b.y - a.y) + a.x
    ) {
      inside = !inside;
    }
  }

  return inside;
}

// Порядок проигрывания выделенного: от края области каждый следующий трек — ближайший
// по звучанию к предыдущему, так что очередь плавно идёт через область, а не прыгает.
export function soundPath<T extends Spot>(spots: T[]): T[] {
  if (spots.length <= 2) return [...spots];

  const left = [...spots];
  let start = 0;
  for (let i = 1; i < left.length; i++) if (left[i].x < left[start].x) start = i;

  const path = [left.splice(start, 1)[0]];

  while (left.length > 0) {
    const last = path[path.length - 1];
    let nearest = 0;
    let best = Infinity;

    for (let i = 0; i < left.length; i++) {
      const distance = (left[i].x - last.x) ** 2 + (left[i].y - last.y) ** 2;
      if (distance < best) {
        best = distance;
        nearest = i;
      }
    }

    path.push(left.splice(nearest, 1)[0]);
  }

  return path;
}

// Если выделено больше, чем играется за раз, — берутся ближайшие к середине области.
export function closestToCentre<T extends Spot>(spots: T[], limit: number): T[] {
  if (spots.length <= limit) return spots;

  const cx = spots.reduce((sum, spot) => sum + spot.x, 0) / spots.length;
  const cy = spots.reduce((sum, spot) => sum + spot.y, 0) / spots.length;

  return [...spots]
    .sort((a, b) => (a.x - cx) ** 2 + (a.y - cy) ** 2 - ((b.x - cx) ** 2 + (b.y - cy) ** 2))
    .slice(0, limit);
}

// Яркость точки — сколько раз трек слушали, на логарифмической шкале: разница между
// «ни разу» и «пять раз» важнее, чем между сотней и двумя.
export function playsLevel(plays: number, maxPlays: number): number {
  if (plays <= 0 || maxPlays <= 0) return 0;
  return Math.log1p(plays) / Math.log1p(maxPlays);
}

export function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)] ?? 0;
}
