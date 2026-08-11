import type { NextConfig } from "next";


const backendUrl = process.env.BACKEND_INTERNAL_URL ?? "http://localhost:5199";
const proxyApiInDev = process.env.NEXT_DISABLE_API_PROXY !== "1";

const nextConfig: NextConfig = {
  output: "standalone",
  reactStrictMode: true,
  poweredByHeader: false,
  allowedDevOrigins: ["192.168.*.*", "10.*.*.*", "172.16.*.*", "*.local"],

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
