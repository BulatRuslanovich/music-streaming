// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";
import { expect, it, vi } from "vitest";

const source = readFileSync(new URL("../../public/sw.js", import.meta.url), "utf8");

it("leaves progressive audio and Range requests to the browser network stack", () => {
  const listeners = new Map<string, (event: unknown) => void>();
  runInNewContext(source, {
    self: {
      location: { origin: "https://music.test" },
      addEventListener: (name: string, listener: (event: unknown) => void) =>
        listeners.set(name, listener),
    },
    URL,
    Set,
    Promise,
  });
  const respondWith = vi.fn((response: Promise<Response>) => void response.catch(() => {}));
  listeners.get("fetch")!({
    request: new Request(
      "https://music.test/api/tracks/00000000-0000-0000-0000-000000000001/stream?quality=Normal",
      { headers: { Range: "bytes=0-" } },
    ),
    respondWith,
  });
  expect(respondWith).not.toHaveBeenCalled();
});

function worker(fetch: () => Promise<Response>, cached: Map<string, Response>) {
  const cache = { match: async (url: string) => cached.get(url), put: vi.fn() };
  const shell = runInNewContext(`${source}\nshell`, {
    self: { addEventListener: vi.fn() },
    caches: { open: async () => cache },
    fetch,
    Response,
    Set,
    Promise,
  }) as (
    event: { waitUntil: (work: Promise<unknown>) => void },
    request: string,
  ) => Promise<Response>;
  return (path: string) => shell({ waitUntil: () => {} }, path);
}

it("does not serve the cached home HTML when opening another route online", async () => {
  const shell = worker(
    async () => new Response("recap page"),
    new Map([["/", new Response("home page")]]),
  );
  expect(await (await shell("/recap")).text()).toBe("recap page");
});

it("keeps the saved route available when the network is unavailable", async () => {
  const shell = worker(
    async () => {
      throw new TypeError("Offline");
    },
    new Map([["/recap", new Response("saved recap")]]),
  );
  expect(await (await shell("/recap")).text()).toBe("saved recap");
});
