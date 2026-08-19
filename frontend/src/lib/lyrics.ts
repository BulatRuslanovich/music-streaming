// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Lyrics } from "./types";

export function toLrc(lyrics: Lyrics): string {
  if (lyrics.lines.length === 0) return lyrics.plain;

  return lyrics.lines.map((line) => `${stamp(line.at)}${line.text}`).join("\n");
}

function stamp(at: number): string {
  const minutes = Math.floor(at / 60_000);
  const seconds = Math.floor((at % 60_000) / 1000);
  const milliseconds = at % 1000;

  const fraction =
    milliseconds % 10 === 0 ? pad(milliseconds / 10) : String(milliseconds).padStart(3, "0");

  return `[${pad(minutes)}:${pad(seconds)}.${fraction}]`;
}

const pad = (value: number) => String(value).padStart(2, "0");
