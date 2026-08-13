import type { SystemInfo } from "./types";

const SHORT_SHA_LENGTH = 7;

export function shortCommit(sha: string | undefined | null): string | undefined {
  if (!sha) return undefined;

  return sha.slice(0, SHORT_SHA_LENGTH);
}

export const frontendBuild: SystemInfo = {
  version: process.env.APP_VERSION || "0.0.0",
  commit: shortCommit(process.env.APP_COMMIT),
  builtAt: process.env.APP_BUILT_AT || undefined,
};
