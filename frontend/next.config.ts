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
  },

  /**
   * Service worker обязан приходить свежим: закэшированный браузером старый файл продолжал бы
   * обслуживать приложение и после обновления, и вернуть управление было бы нечем.
   */
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
