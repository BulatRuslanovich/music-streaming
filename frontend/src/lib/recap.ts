// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { StatisticsEntry, StatisticsTrack } from "./types";

export interface MonthlyRecap {
  month: string;
  timeZone: string;
  listenedSeconds: number;
  plays: number;
  uniqueTracks: number;
  uniqueArtists: number;
  previousListenedSeconds: number;
  topTracks: StatisticsTrack[];
  topArtists: StatisticsEntry[];
  discoveries: StatisticsEntry[];
  // Необязательные, а не просто nullable: API выставлен с `WhenWritingNull`, поэтому пустой
  // жанр не приезжает как null — поля нет вовсе.
  topGenre?: string | null;
  previousTopGenre?: string | null;
}

export function monthLabel(month: string, locale: string): string {
  return new Date(`${month}-01T12:00:00Z`).toLocaleDateString(locale, {
    month: "long",
    year: "numeric",
    timeZone: "UTC",
  });
}

export function listeningChange(current: number, previous: number): number | null {
  return previous > 0 ? Math.round(((current - previous) / previous) * 100) : null;
}

export interface RecapCard {
  eyebrow: string;
  title: string;
  lines: string[];
  /** Обложки топ-треков месяца, до четырёх. */
  covers: (string | null)[];
  filename: string;
}

const CARD_WIDTH = 1080;
const CARD_HEIGHT = 1350;
const MOSAIC_HEIGHT = 810;

/**
 * Карточка, которой делятся. Рисуется обложками месяца и цветами текущей темы, а не
 * фиксированной палитрой: иначе картинка расходится и с приложением, и сама с собой при
 * смене темы — а делятся именно ей, вне всякого приложения.
 */
export async function downloadRecapCard(card: RecapCard): Promise<void> {
  await document.fonts.ready;

  const canvas = document.createElement("canvas");
  canvas.width = CARD_WIDTH;
  canvas.height = CARD_HEIGHT;

  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas unavailable");

  const theme = themeColors();
  context.fillStyle = theme.canvas;
  context.fillRect(0, 0, CARD_WIDTH, CARD_HEIGHT);

  await drawMosaic(context, card.covers, theme.raised);
  drawScrim(context, theme.canvas);
  drawText(context, card, theme);

  await save(canvas, card.filename);
}

interface ThemeColors {
  canvas: string;
  raised: string;
  foreground: string;
  muted: string;
  brand: string;
}

function themeColors(): ThemeColors {
  const style = getComputedStyle(document.documentElement);
  const read = (token: string, fallback: string) =>
    style.getPropertyValue(token).trim() || fallback;

  return {
    canvas: read("--canvas", "#0b0b0d"),
    raised: read("--surface-raised", "#1b1b1f"),
    foreground: read("--foreground", "#f5f5f5"),
    muted: read("--muted-foreground", "#a1a1aa"),
    brand: read("--brand", "#e9b44c"),
  };
}

async function drawMosaic(
  context: CanvasRenderingContext2D,
  covers: (string | null)[],
  empty: string,
): Promise<void> {
  const tiles = covers.slice(0, 4);
  const images = await Promise.all(tiles.map(loadImage));
  const usable = images.filter((image) => image !== null);

  // Мозаика собирается только когда есть все четыре плитки — иначе одна обложка во всю ширину
  // честнее, чем сетка с дырами.
  if (usable.length >= 4) {
    const width = CARD_WIDTH / 2;
    const height = MOSAIC_HEIGHT / 2;
    usable
      .slice(0, 4)
      .forEach((image, index) =>
        drawCover(
          context,
          image,
          (index % 2) * width,
          Math.floor(index / 2) * height,
          width,
          height,
        ),
      );
    return;
  }

  if (usable[0]) {
    drawCover(context, usable[0], 0, 0, CARD_WIDTH, MOSAIC_HEIGHT);
    return;
  }

  context.fillStyle = empty;
  context.fillRect(0, 0, CARD_WIDTH, MOSAIC_HEIGHT);
}

/** Вписывает обложку по короткой стороне и обрезает лишнее, как `object-fit: cover`. */
function drawCover(
  context: CanvasRenderingContext2D,
  image: HTMLImageElement,
  x: number,
  y: number,
  width: number,
  height: number,
): void {
  const scale = Math.max(width / image.width, height / image.height);
  const drawnWidth = image.width * scale;
  const drawnHeight = image.height * scale;

  context.save();
  context.beginPath();
  context.rect(x, y, width, height);
  context.clip();
  context.drawImage(
    image,
    x + (width - drawnWidth) / 2,
    y + (height - drawnHeight) / 2,
    drawnWidth,
    drawnHeight,
  );
  context.restore();
}

function drawScrim(context: CanvasRenderingContext2D, canvasColor: string): void {
  const scrim = context.createLinearGradient(0, MOSAIC_HEIGHT - 320, 0, MOSAIC_HEIGHT);
  scrim.addColorStop(0, "transparent");
  scrim.addColorStop(1, canvasColor);
  context.fillStyle = scrim;
  context.fillRect(0, MOSAIC_HEIGHT - 320, CARD_WIDTH, 320);
}

function drawText(context: CanvasRenderingContext2D, card: RecapCard, theme: ThemeColors): void {
  const margin = 80;
  const width = CARD_WIDTH - margin * 2;

  context.fillStyle = theme.brand;
  context.font = "bold 30px sans-serif";
  context.fillText(card.eyebrow.toUpperCase(), margin, MOSAIC_HEIGHT + 20, width);

  context.fillStyle = theme.foreground;
  context.font = "bold 76px sans-serif";
  context.fillText(card.title, margin, MOSAIC_HEIGHT + 120, width);

  card.lines.slice(0, 5).forEach((line, index) => {
    context.fillStyle = index === 0 ? theme.foreground : theme.muted;
    context.font = index === 0 ? "bold 46px sans-serif" : "34px sans-serif";
    context.fillText(line, margin, MOSAIC_HEIGHT + 220 + index * 72, width);
  });
}

function loadImage(url: string | null): Promise<HTMLImageElement | null> {
  if (!url) return Promise.resolve(null);

  return new Promise((resolve) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => resolve(null);
    image.src = url;
  });
}

async function save(canvas: HTMLCanvasElement, filename: string): Promise<void> {
  const blob = await new Promise<Blob>((resolve, reject) =>
    canvas.toBlob(
      (value) => (value ? resolve(value) : reject(new Error("Image export failed"))),
      "image/png",
    ),
  );

  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
