import type { NextConfig } from "next";

/**
 * In production the reverse proxy (Caddy) routes `/api/*` straight to the ASP.NET Core container,
 * so audio bytes never pass through Node. Only local development needs a rewrite, which keeps the
 * frontend and the API on one origin so the HttpOnly auth cookies apply to both.
 */
const backendUrl = process.env.BACKEND_INTERNAL_URL ?? "http://localhost:5199";
const proxyApiInDev = process.env.NEXT_DISABLE_API_PROXY !== "1";

const nextConfig: NextConfig = {
  // Emits .next/standalone with a minimal server and only the needed node_modules, which keeps
  // the runtime Docker image small.
  output: "standalone",

  reactStrictMode: true,

  // The app is a private library behind authentication; no need to advertise the framework.
  poweredByHeader: false,

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
