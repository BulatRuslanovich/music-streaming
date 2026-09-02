// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { frontendBuild } from "./buildInfo";

const SOURCE_URL = "https://github.com/BulatRuslanovich/music-streaming";

/**
 * Тот же знак, что и в <BrandMark>: открытая C, из апертуры которой выходит волна. Штрихов
 * волны ровно семь — столько же, сколько в SVG.
 */
const MARK = "  ⌒  ▁ ▃ ▆ █ ▆ ▃ ▁\n ( C  A I M A C K\n  ⌣  ▁ ▃ ▆ █ ▆ ▃ ▁";

let printed = false;

export function printConsoleBanner(): void {
  if (printed || typeof window === "undefined") return;
  printed = true;

  const version = frontendBuild.commit
    ? `${frontendBuild.version} · ${frontendBuild.commit}`
    : frontendBuild.version;

  console.log(
    `%c\n${MARK}\n%c\n  ${version}\n  ${SOURCE_URL}\n`,
    "color:#e9b44c;font-weight:bold",
    "color:inherit;opacity:0.65",
  );
}
