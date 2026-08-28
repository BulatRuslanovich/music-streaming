// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { HOME_SECTION_SIZE } from "@/lib/api/contracts";

export const PRELOAD_GLOBAL = "__msPreload";

export const SESSION_HINT_COOKIE = "ms_session";

const PRELOAD_BY_ROUTE: Record<string, string[]> = {
  "/": ["/api/auth/me", `/api/home/feed?sectionSize=${HOME_SECTION_SIZE}`],
};

const FALLBACK_PRELOAD = ["/api/auth/me"];

export const EARLY_FETCH_SCRIPT = `try {
  if (document.cookie.indexOf("${SESSION_HINT_COOKIE}=") !== -1) {
    var byRoute = ${JSON.stringify(PRELOAD_BY_ROUTE)};
    var paths = byRoute[location.pathname] || ${JSON.stringify(FALLBACK_PRELOAD)};
    var store = (window.${PRELOAD_GLOBAL} = {});
    for (var i = 0; i < paths.length; i++) {
      (function (path) {
        store[path] = fetch(path, { credentials: "include" }).catch(function () {
          return null;
        });
      })(paths[i]);
    }
  }
} catch (e) {}`;

type PreloadStore = Record<string, Promise<Response | null> | undefined>;

export async function takePreloaded(url: string): Promise<Response | null> {
  if (typeof window === "undefined") return null;

  const store = (window as unknown as Record<string, PreloadStore | undefined>)[PRELOAD_GLOBAL];
  const pending = store?.[url];
  if (!store || !pending) return null;

  delete store[url];

  const response = await pending;
  return response ?? null;
}
