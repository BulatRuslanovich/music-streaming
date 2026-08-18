const SHELL_CACHE = "caimack-shell-v1";
const ASSET_CACHE = "caimack-assets-v1";
const IMAGE_CACHE = "caimack-images-v1";
const AUDIO_CACHE = "caimack-audio-v1";

const OWN_CACHES = [SHELL_CACHE, ASSET_CACHE, IMAGE_CACHE, AUDIO_CACHE];

const STREAM = /^\/api\/tracks\/[0-9a-f-]+\/stream$/i;
const IMAGE = /^\/api\/(albums|artists|playlists|tracks)\/[0-9a-f-]+\/(cover|image)$/i;

self.addEventListener("install", () => self.skipWaiting());

self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys();

      await Promise.all(
        names
          .filter((name) => name.startsWith("caimack-") && !OWN_CACHES.includes(name))
          .map((name) => caches.delete(name)),
      );

      await self.clients.claim();
    })(),
  );
});

self.addEventListener("message", (event) => {
  if (event.data?.type === "clear-offline") {
    event.waitUntil(Promise.all(OWN_CACHES.map((name) => caches.delete(name))));
  }
});

self.addEventListener("fetch", (event) => {
  const { request } = event;

  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;

  if (STREAM.test(url.pathname)) {
    event.respondWith(serveAudio(request, url));
    return;
  }

  if (IMAGE.test(url.pathname)) {
    event.respondWith(cacheFirst(request, IMAGE_CACHE));
    return;
  }

  if (url.pathname.startsWith("/api/")) return;

  if (url.pathname.startsWith("/_next/static/") || url.pathname.startsWith("/icons/")) {
    event.respondWith(cacheFirst(request, ASSET_CACHE));
    return;
  }

  if (request.mode === "navigate") {
    event.respondWith(shell(request));
  }
});

async function serveAudio(request, url) {
  const cache = await caches.open(AUDIO_CACHE);

  const cached =
    (await cache.match(url.href, { ignoreVary: true })) ??
    (await cache.match(url.href, { ignoreVary: true, ignoreSearch: true }));

  if (!cached) return fetch(request);

  const range = request.headers.get("range");
  if (!range) return cached;

  return partial(cached, range);
}

async function partial(response, range) {
  const buffer = await response.clone().arrayBuffer();
  const size = buffer.byteLength;

  const match = /bytes=(\d*)-(\d*)/.exec(range);
  if (!match) return response;

  const [, from, to] = match;

  const start = from === "" ? Math.max(0, size - Number(to || 0)) : Number(from);
  const end = from === "" || to === "" ? size - 1 : Math.min(Number(to), size - 1);

  if (!Number.isFinite(start) || start >= size || end < start) {
    return new Response(null, {
      status: 416,
      headers: { "Content-Range": `bytes */${size}` },
    });
  }

  const slice = buffer.slice(start, end + 1);

  return new Response(slice, {
    status: 206,
    statusText: "Partial Content",
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "audio/mpeg",
      "Content-Length": String(slice.byteLength),
      "Content-Range": `bytes ${start}-${end}/${size}`,
      "Accept-Ranges": "bytes",
    },
  });
}

async function cacheFirst(request, cacheName) {
  const cache = await caches.open(cacheName);
  const cached = await cache.match(request);
  if (cached) return cached;

  const response = await fetch(request);
  if (response.ok) cache.put(request, response.clone());

  return response;
}

async function shell(request) {
  const cache = await caches.open(SHELL_CACHE);

  try {
    const response = await fetch(request);
    if (response.ok) cache.put(request, response.clone());

    return response;
  } catch {
    return (await cache.match(request)) ?? (await cache.match("/")) ?? Response.error();
  }
}
