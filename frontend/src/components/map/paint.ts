// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { playsLevel, toMap, toScreen, type MapView, type Spot } from "@/lib/libraryMap";
import { rgbOf, rgba, type Rgb } from "@/lib/cssColor";
import type { MapPoint } from "@/lib/types";
import type { MapLabel } from "./LibraryMapCanvas";

const LEVELS = 8;
const TERRITORY = 0.14;

export interface Palette {
  primary: Rgb;
  onPrimary: Rgb;
  faint: Rgb;
  ink: Rgb;
  card: Rgb;
  light: boolean;
  font: string;
}

export function readPalette(element: Element): Palette {
  const style = getComputedStyle(element);
  const read = (name: string) => style.getPropertyValue(name);

  return {
    primary: rgbOf(read("--primary")),
    onPrimary: rgbOf(read("--primary-foreground")),
    faint: rgbOf(read("--faint-foreground")),
    ink: rgbOf(read("--foreground")),
    card: rgbOf(read("--card"), [0, 0, 0]),
    light: style.colorScheme === "light",
    font: style.fontFamily,
  };
}

function moodTint(hue: number, light: boolean, alpha: number): string {
  return `oklch(${light ? 0.58 : 0.72} 0.11 ${hue} / ${alpha})`;
}

function haloSprite(color: Rgb, strength: number): HTMLCanvasElement {
  const size = 64;
  const sprite = document.createElement("canvas");
  sprite.width = size;
  sprite.height = size;

  const context = sprite.getContext("2d")!;
  const glow = context.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  glow.addColorStop(0, rgba(color, strength));
  glow.addColorStop(0.4, rgba(color, strength * 0.35));
  glow.addColorStop(1, rgba(color, 0));
  context.fillStyle = glow;
  context.fillRect(0, 0, size, size);

  return sprite;
}

function dotRadius(k: number): number {
  return Math.min(6, 2.2 + 0.9 * Math.log2(k + 1));
}

export interface HaloCache {
  key: string;
  sprite: HTMLCanvasElement;
}

export interface Scene {
  points: MapPoint[];
  labels: MapLabel[];
  highlightGenre: string | null;
  nowPlayingId: string | null;
  selectedIds: ReadonlySet<string> | null;
  maxPlays: number;
  links: [number, number][];
  hoveredId: string | null;
  lasso: Spot[] | null;
}

export function paintMap(
  context: CanvasRenderingContext2D,
  palette: Palette,
  view: MapView,
  size: { width: number; height: number },
  halo: { current: HaloCache | null },
  scene: Scene,
): void {
  const {
    points,
    labels,
    highlightGenre,
    nowPlayingId,
    selectedIds,
    maxPlays,
    links,
    hoveredId,
    lasso,
  } = scene;
  const { width, height } = size;
  const ratio = window.devicePixelRatio || 1;
  const base = Math.min(width, height);
  const radius = dotRadius(view.k);

  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  context.clearRect(0, 0, width, height);

  const step = 0.1 / 2 ** Math.max(0, Math.floor(Math.log2(view.k)));
  const from = toMap({ x: 0, y: 0 }, view, base);
  const to = toMap({ x: width, y: height }, view, base);
  context.fillStyle = rgba(palette.faint, palette.light ? 0.3 : 0.2);
  context.beginPath();
  for (let gx = Math.floor(from.x / step) * step; gx <= to.x; gx += step) {
    for (let gy = Math.floor(from.y / step) * step; gy <= to.y; gy += step) {
      const spot = toScreen({ x: gx, y: gy }, view, base);
      context.moveTo(spot.x + 1, spot.y);
      context.arc(spot.x, spot.y, 1, 0, Math.PI * 2);
    }
  }
  context.fill();

  for (const label of labels) {
    if (label.hue === undefined) continue;

    const spot = toScreen(label, view, base);
    const reach = TERRITORY * base * view.k;
    const glow = context.createRadialGradient(spot.x, spot.y, 0, spot.x, spot.y, reach);
    glow.addColorStop(0, moodTint(label.hue, palette.light, palette.light ? 0.2 : 0.16));
    glow.addColorStop(1, moodTint(label.hue, palette.light, 0));
    context.fillStyle = glow;
    context.fillRect(spot.x - reach, spot.y - reach, reach * 2, reach * 2);
  }

  context.beginPath();
  for (const [a, b] of links) {
    const start = toScreen(points[a], view, base);
    const end = toScreen(points[b], view, base);
    context.moveTo(start.x, start.y);
    context.lineTo(end.x, end.y);
  }
  context.lineWidth = 1;
  context.strokeStyle = rgba(palette.ink, palette.light ? 0.13 : 0.09);
  context.stroke();

  const emphasised = (point: MapPoint) =>
    selectedIds
      ? selectedIds.has(point.id)
      : highlightGenre
        ? point.genre === highlightGenre
        : null;

  const buckets = new Map<string, Spot[]>();
  const glowing: { spot: Spot; level: number }[] = [];
  const front: Spot[] = [];
  let current: Spot | null = null;
  let ring: Spot | null = null;

  for (const point of points) {
    const spot = toScreen(point, view, base);
    if (spot.x < -radius || spot.y < -radius || spot.x > width + radius || spot.y > height + radius)
      continue;

    if (point.id === nowPlayingId) current = spot;
    if (point.id === hoveredId) ring = spot;

    const marked = emphasised(point);
    if (marked === true) {
      front.push(spot);
      continue;
    }

    let fill: string;
    if (marked === false) {
      fill = rgba(palette.faint, 0.14);
    } else {
      const level = Math.round(playsLevel(point.plays, maxPlays) * LEVELS) / LEVELS;
      fill = level === 0 ? rgba(palette.faint, 0.42) : rgba(palette.primary, 0.3 + 0.7 * level);
      if (level > 0) glowing.push({ spot, level });
    }

    const bucket = buckets.get(fill);
    if (bucket) bucket.push(spot);
    else buckets.set(fill, [spot]);
  }

  const dots = (spots: Spot[], size: number) => {
    context.beginPath();
    for (const spot of spots) {
      context.moveTo(spot.x + size, spot.y);
      context.arc(spot.x, spot.y, size, 0, Math.PI * 2);
    }
    context.fill();
  };

  const haloKey = `${palette.primary.join()}:${palette.light}`;
  if (halo.current?.key !== haloKey) {
    halo.current = {
      key: haloKey,
      sprite: haloSprite(palette.primary, palette.light ? 0.3 : 0.4),
    };
  }
  const sprite = halo.current.sprite;

  context.globalCompositeOperation = palette.light ? "multiply" : "lighter";
  for (const { spot, level } of glowing) {
    const reach = radius * (2 + 3.5 * level);
    context.drawImage(sprite, spot.x - reach, spot.y - reach, reach * 2, reach * 2);
  }
  context.globalCompositeOperation = "source-over";

  for (const [fill, spots] of buckets) {
    context.fillStyle = fill;
    dots(spots, radius);
  }

  if (front.length > 0) {
    context.fillStyle = rgba(palette.primary, 1);
    dots(front, radius + 0.5);
  }

  context.lineWidth = 2;
  context.strokeStyle = rgba(palette.ink, 1);

  if (current) {
    const reach = radius * 9;
    context.drawImage(sprite, current.x - reach, current.y - reach, reach * 2, reach * 2);
    context.fillStyle = rgba(palette.primary, 1);
    dots([current], radius + 1.5);
    context.beginPath();
    context.arc(current.x, current.y, radius + 5, 0, Math.PI * 2);
    context.stroke();
  }

  if (ring) {
    context.beginPath();
    context.arc(ring.x, ring.y, radius + 3, 0, Math.PI * 2);
    context.stroke();
  }

  context.textAlign = "left";
  context.textBaseline = "middle";

  const placed: { left: number; right: number; top: number; bottom: number }[] = [];

  for (const label of [...labels].sort(
    (a, b) => Number(b.strong ?? false) - Number(a.strong ?? false),
  )) {
    const spot = toScreen(label, view, base);
    const size = label.strong ? 14 : 12;
    context.font = `${label.strong ? 700 : 600} ${size}px ${palette.font}`;

    const mark = label.hue === undefined ? 0 : 13;
    const plate = context.measureText(label.text).width + 20 + mark;
    const height = size + 12;
    let y = spot.y;

    for (let attempt = 0; attempt < 6; attempt++) {
      const box = {
        left: spot.x - plate / 2 - 4,
        right: spot.x + plate / 2 + 4,
        top: y - height / 2 - 2,
        bottom: y + height / 2 + 2,
      };
      const hit = placed.find(
        (other) =>
          box.left < other.right &&
          box.right > other.left &&
          box.top < other.bottom &&
          box.bottom > other.top,
      );
      if (!hit) {
        placed.push(box);
        break;
      }
      y = hit.bottom + height / 2 + 2;
    }

    const left = spot.x - plate / 2;
    context.beginPath();
    context.roundRect(left, y - height / 2, plate, height, height / 2);
    context.fillStyle = label.strong ? rgba(palette.primary, 1) : rgba(palette.card, 0.86);
    context.fill();

    if (!label.strong) {
      context.lineWidth = 1;
      context.strokeStyle =
        label.hue === undefined
          ? rgba(palette.ink, 0.16)
          : moodTint(label.hue, palette.light, 0.55);
      context.stroke();
    }

    if (label.hue !== undefined) {
      context.beginPath();
      context.arc(left + 13, y, 3.5, 0, Math.PI * 2);
      context.fillStyle = moodTint(label.hue, palette.light, 1);
      context.fill();
    }

    context.fillStyle = label.strong ? rgba(palette.onPrimary, 1) : rgba(palette.ink, 1);
    context.fillText(label.text, left + 10 + mark, y + 0.5);
  }

  if (lasso && lasso.length > 1) {
    context.beginPath();
    context.moveTo(lasso[0].x, lasso[0].y);
    for (const spot of lasso) context.lineTo(spot.x, spot.y);
    context.closePath();
    context.fillStyle = rgba(palette.primary, 0.08);
    context.fill();
    context.setLineDash([6, 4]);
    context.lineWidth = 1.5;
    context.strokeStyle = rgba(palette.ink, 1);
    context.stroke();
    context.setLineDash([]);
  }
}
