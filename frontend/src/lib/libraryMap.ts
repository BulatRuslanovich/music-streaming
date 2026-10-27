// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export interface Spot {
  x: number;
  y: number;
}

export interface MapView {
  k: number;
  panX: number;
  panY: number;
}

export const MIN_ZOOM = 0.8;
export const MAX_ZOOM = 40;

export const MAX_SELECTION_PLAY = 300;

export interface Bounds {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export function boundsOf(spots: Spot[]): Bounds | null {
  if (spots.length === 0) return null;

  const bounds = { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity };
  for (const spot of spots) {
    bounds.minX = Math.min(bounds.minX, spot.x);
    bounds.minY = Math.min(bounds.minY, spot.y);
    bounds.maxX = Math.max(bounds.maxX, spot.x);
    bounds.maxY = Math.max(bounds.maxY, spot.y);
  }

  return bounds;
}

const FIT_PADDING_PX = 56;
const FIT_PADDING_SHARE = 0.08;
const MIN_FIT_SPAN = 0.05;

export function fitView(width: number, height: number, bounds?: Bounds | null): MapView {
  const base = Math.min(width, height);
  if (!bounds) return { k: 1, panX: (width - base) / 2, panY: (height - base) / 2 };

  const spanX = Math.max(bounds.maxX - bounds.minX, MIN_FIT_SPAN) * base;
  const spanY = Math.max(bounds.maxY - bounds.minY, MIN_FIT_SPAN) * base;
  const padding = Math.min(FIT_PADDING_PX, base * FIT_PADDING_SHARE);
  const fit = Math.min((width - 2 * padding) / spanX, (height - 2 * padding) / spanY);
  const k = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, fit));

  return centreOn(
    { x: (bounds.minX + bounds.maxX) / 2, y: (bounds.minY + bounds.maxY) / 2 },
    k,
    width,
    height,
  );
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

export function closestToCentre<T extends Spot>(spots: T[], limit: number): T[] {
  if (spots.length <= limit) return spots;

  const cx = spots.reduce((sum, spot) => sum + spot.x, 0) / spots.length;
  const cy = spots.reduce((sum, spot) => sum + spot.y, 0) / spots.length;

  return [...spots]
    .sort((a, b) => (a.x - cx) ** 2 + (a.y - cy) ** 2 - ((b.x - cx) ** 2 + (b.y - cy) ** 2))
    .slice(0, limit);
}

export function playsLevel(plays: number, maxPlays: number): number {
  if (plays <= 0 || maxPlays <= 0) return 0;
  return Math.log1p(plays) / Math.log1p(maxPlays);
}

export function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)] ?? 0;
}

export function constellation(spots: Spot[], neighbours = 2): [number, number][] {
  if (spots.length < 2) return [];

  const nearest = spots.map((spot, i) => {
    const found: { j: number; distance: number }[] = [];

    for (let j = 0; j < spots.length; j++) {
      if (j === i) continue;

      const distance = (spots[j].x - spot.x) ** 2 + (spots[j].y - spot.y) ** 2;
      if (found.length < neighbours || distance < found[found.length - 1].distance) {
        found.push({ j, distance });
        found.sort((a, b) => a.distance - b.distance);
        if (found.length > neighbours) found.pop();
      }
    }

    return found;
  });

  const reach = median(nearest.map((found) => found[0].distance)) * LINK_REACH ** 2;
  const pairs = new Set<string>();
  const links: [number, number][] = [];

  nearest.forEach((found, i) => {
    for (const { j, distance } of found) {
      const key = i < j ? `${i}:${j}` : `${j}:${i}`;
      if (distance > reach || pairs.has(key)) continue;

      pairs.add(key);
      links.push(i < j ? [i, j] : [j, i]);
    }
  });

  return links;
}

const LINK_REACH = 2.5;
