// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { StatisticsEntry, StatisticsTrack } from "./types";

export interface MonthlyRecap {
  month: string;
  timeZone: string;
  isComplete: boolean;
  listenedSeconds: number;
  plays: number;
  uniqueTracks: number;
  uniqueArtists: number;
  previousListenedSeconds: number;
  topTracks: StatisticsTrack[];
  topArtists: StatisticsEntry[];
  discoveries: StatisticsEntry[];
  topGenre: string | null;
  previousTopGenre: string | null;
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

export async function downloadRecapCard(title: string, lines: string[], filename: string) {
  await document.fonts.ready;
  const canvas = document.createElement("canvas");
  canvas.width = 1080;
  canvas.height = 1350;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas unavailable");
  const gradient = context.createLinearGradient(0, 0, 1080, 1350);
  gradient.addColorStop(0, "#312e81");
  gradient.addColorStop(0.55, "#172554");
  gradient.addColorStop(1, "#09090b");
  context.fillStyle = gradient;
  context.fillRect(0, 0, 1080, 1350);
  context.fillStyle = "#c4b5fd";
  context.font = "32px sans-serif";
  context.fillText("CAIMACK / RECAP", 80, 120);
  context.fillStyle = "#ffffff";
  context.font = "bold 64px sans-serif";
  context.fillText(title, 80, 250, 920);
  lines.slice(0, 7).forEach((line, index) => {
    context.font = index === 0 ? "bold 54px sans-serif" : "36px sans-serif";
    context.fillStyle = index === 0 ? "#c4b5fd" : "#e2e8f0";
    context.fillText(line, 80, 410 + index * 115, 920);
  });
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
