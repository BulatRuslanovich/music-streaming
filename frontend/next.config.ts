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

  // In development Next blocks cross-origin requests to its dev-only assets, which includes
  // hot-reload traffic when the page is opened from a phone at http://<home-ip>:3000 instead of
  // localhost. These patterns cover the usual private ranges so testing on a device just works.
  // Development only — it has no effect on the production build.
  allowedDevOrigins: ["192.168.*.*", "10.*.*.*", "172.16.*.*", "*.local"],

  experimental: {
    // Only relevant to the development rewrite below. Next buffers a proxied request body in
    // memory and silently truncates it past this limit (10 MB by default), which makes an upload
    // arrive as a malformed multipart body and the connection hang up. Raised enough to drop a
    // whole album in at once during development; production is unaffected because Caddy proxies
    // /api straight to the API and Node never sees the bytes.
    proxyClientMaxBodySize: "512mb",
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
