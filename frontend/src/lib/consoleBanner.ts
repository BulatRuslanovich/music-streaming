// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { frontendBuild } from "./buildInfo";

const SOURCE_URL = "https://github.com/BulatRuslanovich/music-streaming";

const MARK = "C A I M A C K";

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
