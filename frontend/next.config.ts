import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import type { NextConfig } from "next";


const backendUrl = process.env.BACKEND_INTERNAL_URL ?? "http://localhost:5199";
const proxyApiInDev = process.env.NEXT_DISABLE_API_PROXY !== "1";

/**
 * Хеш коммита, из которого собран фронтенд. В образе .git недоступен, поэтому значение приходит
 * build-аргументом; локально его проще спросить у самого git, чем требовать переменную окружения.
 */
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
  } catch {
    // Ниже отдадим заглушку — из-за подвала сборка падать не должна.
  }

  return "0.0.0";
}

const nextConfig: NextConfig = {
  output: "standalone",
  reactStrictMode: true,
  poweredByHeader: false,
  allowedDevOrigins: ["192.168.*.*", "10.*.*.*", "172.16.*.*", "*.local"],

  // Инлайнится в бандл на этапе сборки. Обращаться можно только полным `process.env.APP_VERSION` —
  // деструктуризация не переживает подстановку.
  env: {
    APP_VERSION: resolveVersion(),
    APP_COMMIT: resolveCommit(),
    APP_BUILT_AT: new Date().toISOString(),
  },

  experimental: {
    proxyClientMaxBodySize: "1gb",
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
