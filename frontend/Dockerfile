# ==============================================================================================
# Next.js frontend — multi-stage build using the standalone output
# ==============================================================================================

FROM node:22-alpine AS deps
WORKDIR /app

# Install from the lockfile only, so this layer is cached until dependencies change.
COPY package.json package-lock.json ./
RUN npm ci

# ----------------------------------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

COPY --from=deps /app/node_modules ./node_modules
COPY . .

ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

# ----------------------------------------------------------------------------------------------
FROM node:22-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0

RUN addgroup -S -g 1001 nodejs && adduser -S -u 1001 -G nodejs nextjs

# `output: "standalone"` traces exactly the files the server needs; static assets and public
# files are copied in separately because the minimal server does not bundle them.
COPY --from=build --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=build --chown=nextjs:nodejs /app/public ./public

USER nextjs
EXPOSE 3000

HEALTHCHECK --interval=15s --timeout=4s --start-period=25s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:3000/login || exit 1

CMD ["node", "server.js"]
