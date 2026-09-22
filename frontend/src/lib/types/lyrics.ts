// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export interface LyricLine {
  at: number;
  text: string;
}

export interface Lyrics {
  trackId: string;
  plain: string;
  lines: LyricLine[];
  source: "Embedded" | "Manual" | "Provider";
}
