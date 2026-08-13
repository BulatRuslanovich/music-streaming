import type { SystemInfo } from "./types";

/** Столько символов хеша хватает, чтобы найти коммит; ровно столько же режет бэкенд. */
const SHORT_SHA_LENGTH = 7;

export function shortCommit(sha: string | undefined | null): string | undefined {
  if (!sha) return undefined;

  return sha.slice(0, SHORT_SHA_LENGTH);
}

/**
 * Метаданные сборки фронтенда. Значения подставляет next.config.ts на этапе сборки, поэтому
 * обращения обязаны быть полными — `process.env.APP_VERSION`, а не деструктуризация.
 */
export const frontendBuild: SystemInfo = {
  version: process.env.APP_VERSION || "0.0.0",
  commit: shortCommit(process.env.APP_COMMIT),
  builtAt: process.env.APP_BUILT_AT || undefined,
};
