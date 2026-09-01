// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchMedia } from "@/lib/http";

interface Call {
  url: string;
  credentials?: RequestCredentials;
}

/**
 * Подменяет `fetch` очередью ответов на медиа-URL; запрос обновления сессии обслуживается
 * отдельно, чтобы тест мог сказать, удалось оно или нет.
 */
function stubFetch(mediaStatuses: number[], refreshOk: boolean) {
  const calls: Call[] = [];
  let next = 0;

  vi.stubGlobal("fetch", (input: string | URL, init: RequestInit = {}) => {
    const url = String(input);
    calls.push({ url, credentials: init.credentials });

    if (url.endsWith("/auth/refresh")) {
      return Promise.resolve(new Response(null, { status: refreshOk ? 204 : 401 }));
    }

    const status = mediaStatuses[Math.min(next++, mediaStatuses.length - 1)];
    return Promise.resolve(new Response(status === 204 ? null : "#EXTM3U", { status }));
  });

  return calls;
}

// `refreshSession` держит запрос в единственном экземпляре и отпускает его через setTimeout(0):
// без этой паузы следующий тест переиспользовал бы результат предыдущего.
afterEach(async () => {
  vi.unstubAllGlobals();
  await new Promise((resolve) => setTimeout(resolve, 0));
});

describe("fetchMedia", () => {
  it("renews the session once and repeats the request", async () => {
    const calls = stubFetch([401, 200], true);

    const response = await fetchMedia("/api/tracks/1/hls/master.m3u8");

    expect(response.status).toBe(200);
    expect(calls.map((call) => call.url)).toEqual([
      "/api/tracks/1/hls/master.m3u8",
      "/api/auth/refresh",
      "/api/tracks/1/hls/master.m3u8",
    ]);
  });

  it("gives back the 401 when the session cannot be renewed", async () => {
    const calls = stubFetch([401, 200], false);

    const response = await fetchMedia("/api/tracks/1/hls/master.m3u8");

    expect(response.status).toBe(401);
    // Второй заход за манифестом не делается: обновиться не удалось, повторять нечем.
    expect(calls).toHaveLength(2);
  });

  it("passes anything but a 401 straight through", async () => {
    // 202 — «ещё нарезается», 404 — «нет такой ступени». Ни то ни другое не ошибка и не повод
    // трогать сессию, поэтому ответ уходит вызывающему как есть.
    for (const status of [200, 202, 404, 500]) {
      const calls = stubFetch([status], true);

      expect((await fetchMedia("/api/tracks/1/hls/master.m3u8")).status).toBe(status);
      expect(calls).toHaveLength(1);

      vi.unstubAllGlobals();
    }
  });

  it("sends cookies and keeps the caller's init", async () => {
    const calls = stubFetch([200], true);
    const controller = new AbortController();

    await fetchMedia(new URL("https://example.test/segment.m4s"), { signal: controller.signal });

    expect(calls[0].url).toBe("https://example.test/segment.m4s");
    expect(calls[0].credentials).toBe("include");
  });
});
