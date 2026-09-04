// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import type { NextConfig } from "next";

const backendUrl = process.env.BACKEND_INTERNAL_URL ?? "http://localhost:5199";
const proxyApiInDev = process.env.NEXT_DISABLE_API_PROXY !== "1";

function resolveCommit(): string {
  if (process.env.GIT_SHA) return process.env.GIT_SHA;

  try {
    return execFileSync("git", ["rev-parse", "--short", "HEAD"], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
    }).trim();
  } catch {
    return "";
  }
}

function resolveVersion(): string {
  try {
    const pkg: unknown = JSON.parse(readFileSync(new URL("package.json", import.meta.url), "utf8"));

    if (pkg && typeof pkg === "object" && "version" in pkg && typeof pkg.version === "string") {
      return pkg.version;
    }
  } catch {}

  return "0.0.0";
}

const nextConfig: NextConfig = {
  output: "standalone",
  reactStrictMode: true,
  poweredByHeader: false,
  allowedDevOrigins: ["192.168.*.*", "10.*.*.*", "172.16.*.*", "*.local"],

  env: {
    APP_VERSION: resolveVersion(),
    APP_COMMIT: resolveCommit(),
    APP_BUILT_AT: new Date().toISOString(),
  },

  experimental: {
    proxyClientMaxBodySize: "1gb",

    // CLI-режим Next закрывает процесс TypeScript 6 раньше, чем дочитывает большой вывод
    // `--showConfig`, и production build падает на обрезанном JSON. Compiler API не гоняет
    // конфигурацию через stdout и остаётся эквивалентной проверкой типов.
    useTypeScriptCli: false,

    // Эти пакеты экспортируют сотни модулей через один barrel-файл, и в бандл затягивалось
    // заметно больше, чем реально используется.
    optimizePackageImports: ["lucide-react", "motion", "@dnd-kit/core", "@dnd-kit/sortable"],

    // Роутер-кэш App Router: возврат назад не должен перезапрашивать только что показанное.
    staleTimes: {
      dynamic: 60,
      static: 300,
    },
  },

  async headers() {
    return [
      {
        source: "/sw.js",
        headers: [
          { key: "Cache-Control", value: "no-cache, no-store, must-revalidate" },
          { key: "Content-Type", value: "application/javascript; charset=utf-8" },
        ],
      },
    ];
  },

  async rewrites() {
    if (process.env.NODE_ENV === "production" || !proxyApiInDev) {
      return [];
    }

    return [
      {
        source: "/api/:path*",
        destination: `${backendUrl}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
