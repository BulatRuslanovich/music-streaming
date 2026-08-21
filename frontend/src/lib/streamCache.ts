// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { mediaUrl } from "@/lib/media";
import type { AdaptiveQuality } from "@/lib/adaptivePlayback";

const STABLE_WINDOW_MS = 30_000;

export interface PrefetchReadiness {
  online: boolean;
  playing: boolean;
  position: number;
  bufferedUntil: number;
  duration: number;
  lastStallAt: number;
  now: number;
}

export function readyToPrefetch(state: PrefetchReadiness): boolean {
  if (!state.online || !state.playing || state.duration <= 0) return false;
  if (state.lastStallAt > 0 && state.now - state.lastStallAt < STABLE_WINDOW_MS) return false;

  const remaining = Math.max(0, state.duration - state.position);
  const required = Math.min(60, remaining);
  return state.bufferedUntil - state.position >= required;
}

export async function prefetchHlsTracks(
  trackIds: string[],
  quality: AdaptiveQuality,
  signal: AbortSignal,
): Promise<boolean> {
  for (const trackId of trackIds) {
    if (!(await prefetchTrack(trackId, quality, signal))) return false;
  }
  return true;
}

export function pinStreamTracks(trackIds: string[]): void {
  postToStreamWorker({ type: "pin-stream-tracks", trackIds });
}

export async function clearStreamCache(): Promise<void> {
  if ("caches" in window) await caches.delete("caimack-hls-v1");
  if ("indexedDB" in window) {
    await Promise.all([
      deleteBrowserDatabase("caimack-stream-cache"),
      deleteBrowserDatabase("caimack-offline"),
    ]);
  }
  postToStreamWorker({ type: "clear-stream-cache" });
}

function postToStreamWorker(message: unknown): void {
  if (!("serviceWorker" in navigator)) return;
  if (navigator.serviceWorker.controller) {
    navigator.serviceWorker.controller.postMessage(message);
    return;
  }

  void navigator.serviceWorker.ready.then((registration) =>
    registration.active?.postMessage(message),
  );
}

function deleteBrowserDatabase(name: string): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase(name);
    request.onsuccess = () => resolve();
    request.onerror = () => resolve();
    request.onblocked = () => resolve();
  });
}

export function registerStreamWorker(): void {
  if ("serviceWorker" in navigator) {
    void navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(() => {});
  }
}

async function prefetchTrack(
  trackId: string,
  quality: AdaptiveQuality,
  signal: AbortSignal,
): Promise<boolean> {
  try {
    const master = await fetch(mediaUrl.hls(trackId, quality), {
      credentials: "include",
      signal,
    });
    if (!master.ok || master.status === 202) return false;

    const masterText = await master.text();
    const variants = playlistUris(masterText);
    const suffix = `${quality.toLowerCase()}/index.m3u8`;
    const variant = variants.find((uri) => uri.toLowerCase().endsWith(suffix));
    if (!variant) return false;

    const media = await fetch(new URL(variant, master.url), { credentials: "include", signal });
    if (!media.ok) return false;

    const mediaText = await media.text();
    const base = media.url;
    const init = /#EXT-X-MAP:URI="([^"]+)"/.exec(mediaText)?.[1];
    const resources = [...(init ? [init] : []), ...playlistUris(mediaText)];

    for (const resource of resources) {
      const response = await fetch(new URL(resource, base), { credentials: "include", signal });
      if (!response.ok) return false;
      await response.arrayBuffer();
    }

    return true;
  } catch (reason) {
    if (reason instanceof DOMException && reason.name === "AbortError") throw reason;
    return false;
  }
}

function playlistUris(playlist: string): string[] {
  return playlist
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith("#"));
}
