// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

const SHELL_CACHE = "caimack-shell-v1";
const ASSET_CACHE = "caimack-assets-v1";
const IMAGE_CACHE = "caimack-images-v1";
const HLS_CACHE = "caimack-hls-v1";
const LEGACY_AUDIO_CACHE = "caimack-audio-v1";

const CACHE_DATABASE = "caimack-stream-cache";
const CACHE_STORE = "entries";
const CACHE_DATABASE_VERSION = 1;
const CACHE_BUDGET = 250 * 1024 * 1024;

const OWN_CACHES = [SHELL_CACHE, ASSET_CACHE, IMAGE_CACHE, HLS_CACHE];
const IMAGE = /^\/api\/(albums|artists|playlists|tracks)\/[0-9a-f-]+\/(cover|image)$/i;
const HLS = /^\/api\/tracks\/([0-9a-f-]+)\/hls\//i;

let pinnedTracks = new Set();
let maintenance = Promise.resolve();

// Сколько байт лежит в кэше. Считается один раз полным проходом, дальше растёт
// по мере записи: getAll() на каждый сегмент — это тысячи записей раз в четыре
// секунды, и на Android такой проход стоит дороже, чем сама загрузка сегмента.
let totalBytes = null;

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

      await caches.delete(LEGACY_AUDIO_CACHE);
      await deleteDatabase("caimack-offline");
      await self.clients.claim();
    })(),
  );
});

self.addEventListener("message", (event) => {
  if (event.data?.type === "clear-stream-cache") {
    pinnedTracks = new Set();
    totalBytes = null;
    event.waitUntil(Promise.all([caches.delete(HLS_CACHE), deleteDatabase(CACHE_DATABASE)]));
    return;
  }

  if (event.data?.type === "pin-stream-tracks") {
    pinnedTracks = new Set(Array.isArray(event.data.trackIds) ? event.data.trackIds : []);
  }
});

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;

  const hlsMatch = HLS.exec(url.pathname);
  if (hlsMatch) {
    const trackId = hlsMatch[1];
    event.respondWith(
      url.pathname.endsWith(".m3u8")
        ? playlist(event, request, trackId)
        : segment(event, request, trackId),
    );
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

  if (request.mode === "navigate") event.respondWith(shell(request));
});

// Запись в кэш и учёт места уезжают в waitUntil, а не в await: пока сегмент ждал
// cache.put(), putEntry() и уборку по бюджету, буфер плеера продолжал таять. Для
// hls.js ответ должен приходить ровно тогда, когда байты уже есть.
async function playlist(event, request, trackId) {
  const cache = await caches.open(HLS_CACHE);

  try {
    const response = await fetch(request);
    if (response.ok && response.status !== 202) {
      event.waitUntil(store(cache, request, response.clone(), trackId));
    }
    return response;
  } catch {
    const cached = await cache.match(request, { ignoreVary: true });
    if (!cached) return Response.error();
    event.waitUntil(touch(request.url));
    return cached;
  }
}

async function segment(event, request, trackId) {
  const cache = await caches.open(HLS_CACHE);
  const cached = await cache.match(request, { ignoreVary: true });
  if (cached) {
    event.waitUntil(touch(request.url));
    return cached;
  }

  const response = await fetch(request);
  if (response.ok && response.status === 200) {
    event.waitUntil(store(cache, request, response.clone(), trackId));
  }
  return response;
}

async function store(cache, request, response, trackId) {
  const measured = response.clone();
  await cache.put(request, response);

  const fromHeader = Number(measured.headers.get("Content-Length") ?? 0);
  const bytes = fromHeader > 0 ? fromHeader : (await measured.arrayBuffer()).byteLength;
  await putEntry({ url: request.url, trackId, bytes, touchedAt: Date.now() });
  totalBytes = (await knownTotal()) + bytes;

  maintenance = maintenance.then(enforceBudget, enforceBudget);
  await maintenance;
}

async function knownTotal() {
  if (totalBytes === null) {
    const entries = await allEntries();
    totalBytes = entries.reduce((sum, entry) => sum + entry.bytes, 0);
  }
  return totalBytes;
}

// Полный проход по записям нужен только чтобы выбрать жертву, то есть при выходе
// за бюджет — а это в поездке случается раз в несколько часов, не раз в сегмент.
async function enforceBudget() {
  if ((await knownTotal()) <= CACHE_BUDGET) return;
  totalBytes = await evict();
}

async function evict() {
  const entries = await allEntries();
  let total = entries.reduce((sum, entry) => sum + entry.bytes, 0);
  if (total <= CACHE_BUDGET) return total;

  const cache = await caches.open(HLS_CACHE);
  const groups = new Map();

  for (const entry of entries) {
    const group = groups.get(entry.trackId) ?? { touchedAt: 0, entries: [] };
    group.touchedAt = Math.max(group.touchedAt, entry.touchedAt);
    group.entries.push(entry);
    groups.set(entry.trackId, group);
  }

  const candidates = [...groups.entries()]
    .filter(([trackId]) => !pinnedTracks.has(trackId))
    .sort((left, right) => left[1].touchedAt - right[1].touchedAt);

  for (const [, group] of candidates) {
    for (const entry of group.entries) {
      await cache.delete(entry.url, { ignoreVary: true });
      await deleteEntry(entry.url);
      total -= entry.bytes;
    }
    if (total <= CACHE_BUDGET) return total;
  }

  const rolling = entries
    .filter((entry) => pinnedTracks.has(entry.trackId))
    .sort((left, right) => left.touchedAt - right.touchedAt);

  for (const entry of rolling) {
    await cache.delete(entry.url, { ignoreVary: true });
    await deleteEntry(entry.url);
    total -= entry.bytes;
    if (total <= CACHE_BUDGET) return total;
  }

  return total;
}

async function cacheFirst(request, cacheName) {
  const cache = await caches.open(cacheName);
  const cached = await cache.match(request);
  if (cached) return cached;

  const response = await fetch(request);
  if (response.ok) await cache.put(request, response.clone());
  return response;
}

async function shell(request) {
  const cache = await caches.open(SHELL_CACHE);

  try {
    const response = await fetch(request);
    if (response.ok) await cache.put(request, response.clone());
    return response;
  } catch {
    return (await cache.match(request)) ?? (await cache.match("/")) ?? Response.error();
  }
}

function openDatabase() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(CACHE_DATABASE, CACHE_DATABASE_VERSION);
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(CACHE_STORE)) {
        request.result.createObjectStore(CACHE_STORE, { keyPath: "url" });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function withStore(mode, action) {
  const database = await openDatabase();
  return new Promise((resolve, reject) => {
    const transaction = database.transaction(CACHE_STORE, mode);
    const request = action(transaction.objectStore(CACHE_STORE));
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
    transaction.oncomplete = () => database.close();
  });
}

function putEntry(entry) {
  return withStore("readwrite", (store) => store.put(entry));
}

function deleteEntry(url) {
  return withStore("readwrite", (store) => store.delete(url));
}

function allEntries() {
  return withStore("readonly", (store) => store.getAll());
}

async function touch(url) {
  const entry = await withStore("readonly", (store) => store.get(url));
  if (entry) await putEntry({ ...entry, touchedAt: Date.now() });
}

function deleteDatabase(name) {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase(name);
    request.onsuccess = () => resolve();
    request.onerror = () => resolve();
    request.onblocked = () => resolve();
  });
}
