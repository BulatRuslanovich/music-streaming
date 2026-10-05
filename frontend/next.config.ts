// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { readFileSync } from "node:fs";
import type { NextConfig } from "next";

const backendUrl = process.env.BACKEND_INTERNAL_URL ?? "http://localhost:5199";
const proxyApiInDev = process.env.NEXT_DISABLE_API_PROXY !== "1";

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
  typedRoutes: true,

  devIndicators: { position: "top-right" },

  logging: {
    browserToTerminal: true,
  },

  env: {
    APP_VERSION: resolveVersion(),
  },

  // Types are checked by `npm run typecheck` in CI; the build skips it to fit a small server.
  typescript: { ignoreBuildErrors: true },

  experimental: {
    proxyClientMaxBodySize: "1gb",

    inlineCss: true,

    useTypeScriptCli: false,

    optimizePackageImports: ["lucide-react", "@dnd-kit/core", "@dnd-kit/sortable"],

    staleTimes: {
      dynamic: 60,
      static: 300,
    },

    requestInsights: true,

    webVitalsAttribution: ["LCP", "INP", "CLS"],
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
