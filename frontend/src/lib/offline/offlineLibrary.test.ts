// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import {
  createOfflineLibrary,
  type OfflineRecord,
  type OfflineStorageAdapter,
} from "@/lib/offline/offlineLibrary";
import type { Track } from "@/lib/types";

const track: Track = {
  id: "11111111-1111-1111-1111-111111111111",
  title: "Offline song",
  artistId: "22222222-2222-2222-2222-222222222222",
  artistName: "Offline artist",
  durationSeconds: 180,
  originalFileName: "offline.flac",
  isFavorite: false,
  hasCover: false,
  hasLyrics: false,
  createdAt: "2026-01-01T00:00:00Z",
};

const masterUrl = `/api/tracks/${track.id}/hls/master.m3u8?maxQuality=Normal`;
const playlistUrl = `/api/tracks/${track.id}/hls/normal/index.m3u8`;
const initUrl = `/api/tracks/${track.id}/hls/normal/init.mp4`;
const firstSegmentUrl = `/api/tracks/${track.id}/hls/normal/segment-00001.m4s`;
const secondSegmentUrl = `/api/tracks/${track.id}/hls/normal/segment-00002.m4s`;

const highMasterUrl = `/api/tracks/${track.id}/hls/master.m3u8?maxQuality=High`;
const highPlaylistUrl = `/api/tracks/${track.id}/hls/high/index.m3u8`;
const highInitUrl = `/api/tracks/${track.id}/hls/high/init.mp4`;
const highSegmentUrl = `/api/tracks/${track.id}/hls/high/segment-00001.m4s`;

const master = `#EXTM3U
#EXT-X-STREAM-INF:BANDWIDTH=128000
normal/index.m3u8
`;

const masterWithHigh = `#EXTM3U
#EXT-X-STREAM-INF:BANDWIDTH=128000
normal/index.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=192000
high/index.m3u8
`;

const highPlaylist = `#EXTM3U
#EXT-X-MAP:URI="init.mp4"
#EXTINF:4.0,
segment-00001.m4s
#EXT-X-ENDLIST
`;

const playlist = `#EXTM3U
#EXT-X-MAP:URI="init.mp4"
#EXTINF:4.0,
segment-00001.m4s
#EXTINF:4.0,
segment-00002.m4s
#EXT-X-ENDLIST
`;

class MemoryOfflineStorage implements OfflineStorageAdapter {
  private readonly records = new Map<string, OfflineRecord>();
  private readonly resources = new Map<string, Response>();

  async load(userId: string): Promise<OfflineRecord[]> {
    return [...this.records.values()].filter((record) => record.userId === userId);
  }

  async save(record: OfflineRecord): Promise<void> {
    this.records.set(`${record.userId}:${record.track.id}`, structuredClone(record));
  }

  get storedUrls(): string[] {
    return [...this.resources.keys()];
  }

  async remove(record: OfflineRecord): Promise<void> {
    await this.dropResources(record.resourceUrls);
    this.records.delete(`${record.userId}:${record.track.id}`);
  }

  async dropResources(urls: string[]): Promise<void> {
    for (const url of urls) this.resources.delete(url);
  }

  async hasResource(url: string): Promise<boolean> {
    return this.resources.has(url);
  }

  async putResource(url: string, response: Response): Promise<number> {
    const bytes = (await response.clone().arrayBuffer()).byteLength;
    this.resources.set(url, response.clone());
    return bytes;
  }

  async requestPersistence(): Promise<boolean> {
    return true;
  }
}

function media(responses: Record<string, string | Error>) {
  return async (url: string | URL): Promise<Response> => {
    const key = String(url);
    const value = responses[key];
    if (value instanceof Error) throw value;
    if (value === undefined) return new Response(null, { status: 404 });
    return new Response(value, { status: 200, headers: { ETag: `"${key}"` } });
  };
}

function completeResponses(): Record<string, string | Error> {
  return {
    [masterUrl]: master,
    [playlistUrl]: playlist,
    [initUrl]: "init",
    [firstSegmentUrl]: "one",
    [secondSegmentUrl]: "two",
  };
}

describe("offline library", () => {
  it("makes a track playable only after every HLS resource is stored", async () => {
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      fetchMedia: media(completeResponses()),
    });

    expect(await library.resolve(track.id)).toBeNull();

    await library.download({ tracks: [track], quality: "Normal" });

    expect(await library.resolve(track.id)).toEqual({ playlistUrl, quality: "Normal" });
    expect(library.getSnapshot().tracks).toEqual([
      expect.objectContaining({
        track,
        state: "ready",
        quality: "Normal",
        resourceUrls: [playlistUrl, initUrl, firstSegmentUrl, secondSegmentUrl],
      }),
    ]);
  });

  it("does not resolve a partially downloaded track", async () => {
    const responses = completeResponses();
    responses[secondSegmentUrl] = new Error("connection lost");
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      fetchMedia: media(responses),
    });

    await expect(library.download({ tracks: [track], quality: "Normal" })).rejects.toThrow(
      "connection lost",
    );

    expect(await library.resolve(track.id)).toBeNull();
    expect(library.getSnapshot().tracks[0]).toEqual(
      expect.objectContaining({
        state: "failed",
        resourceUrls: [playlistUrl, initUrl, firstSegmentUrl],
      }),
    );
  });

  it("waits while the requested HLS rendition is being prepared", async () => {
    let masterAttempts = 0;
    const responses = completeResponses();
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      wait: async () => {},
      fetchMedia: async (url) => {
        if (String(url) === masterUrl && masterAttempts++ === 0) {
          return new Response(null, { status: 202 });
        }
        return media(responses)(url);
      },
    });

    await library.download({ tracks: [track], quality: "Normal" });

    expect(masterAttempts).toBe(2);
    expect(await library.resolve(track.id)).toEqual({ playlistUrl, quality: "Normal" });
  });

  it("finishes an interrupted download when it is retried", async () => {
    const storage = new MemoryOfflineStorage();
    const responses = completeResponses();
    responses[secondSegmentUrl] = new Error("connection lost");
    const library = await createOfflineLibrary({
      userId: "listener",
      storage,
      fetchMedia: media(responses),
    });

    await expect(library.download({ tracks: [track], quality: "Normal" })).rejects.toThrow();
    responses[secondSegmentUrl] = "two";
    await library.download({ tracks: [track], quality: "Normal" });

    expect(await library.resolve(track.id)).toEqual({ playlistUrl, quality: "Normal" });
  });

  it("removes a downloaded track from the offline library", async () => {
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      fetchMedia: media(completeResponses()),
    });
    await library.download({ tracks: [track], quality: "Normal" });

    await library.remove([track.id]);

    expect(await library.resolve(track.id)).toBeNull();
    expect(library.getSnapshot().tracks).toEqual([]);
  });

  it("skips tracks that are already downloaded at the requested quality", async () => {
    let online = true;
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      fetchMedia: async (url) => {
        if (!online) throw new Error("the second request should not need the network");
        return media(completeResponses())(url);
      },
    });
    await library.download({ tracks: [track], quality: "Normal" });

    online = false;
    await library.download({ tracks: [track], quality: "Normal" });

    expect(await library.resolve(track.id)).toEqual({ playlistUrl, quality: "Normal" });
  });

  it("continues downloading a collection when earlier tracks fail", async () => {
    const first = { ...track, id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", title: "First" };
    const second = { ...track, id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", title: "Second" };
    const last = { ...track, id: "cccccccc-cccc-cccc-cccc-cccccccccccc", title: "Last" };
    const lastMaster = `/api/tracks/${last.id}/hls/master.m3u8?maxQuality=Normal`;
    const lastPlaylist = `/api/tracks/${last.id}/hls/normal/index.m3u8`;

    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      fetchMedia: async (url) => {
        const key = String(url);
        if (key.includes(first.id) || key.includes(second.id)) throw new Error("unavailable");
        if (key === lastMaster) return new Response(master);
        if (key === lastPlaylist) return new Response(playlist);
        return new Response("media");
      },
    });

    await expect(
      library.download({ tracks: [first, second, last], quality: "Normal" }),
    ).rejects.toThrow("unavailable");

    expect(await library.resolve(last.id)).toEqual({
      playlistUrl: lastPlaylist,
      quality: "Normal",
    });
  });

  it("cancels an active download when its record is removed", async () => {
    let segmentStarted!: () => void;
    const started = new Promise<void>((resolve) => {
      segmentStarted = resolve;
    });
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      fetchMedia: async (url, init) => {
        const key = String(url);
        if (key === masterUrl) return new Response(master);
        if (key === playlistUrl) return new Response(playlist);
        if (key === initUrl) return new Response("init");

        segmentStarted();
        return new Promise<Response>((_, reject) => {
          init?.signal?.addEventListener(
            "abort",
            () => reject(new DOMException("cancelled", "AbortError")),
            { once: true },
          );
        });
      },
    });

    const downloading = library.download({ tracks: [track], quality: "Normal" });
    await started;
    await library.remove([track.id]);

    await expect(downloading).rejects.toThrow("cancelled");
    expect(library.getSnapshot().tracks).toEqual([]);
  });

  it("settles for the best ready rendition when the requested one keeps lagging", async () => {
    let masterAttempts = 0;
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      wait: async () => {},
      fetchMedia: async (url) => {
        // Мастер отдаёт 200 уже с одной готовой вариацией, и High среди них так и не появляется.
        if (String(url) === highMasterUrl) {
          masterAttempts += 1;
          return new Response(master);
        }
        return media(completeResponses())(url);
      },
    });

    await library.download({ tracks: [track], quality: "High" });

    expect(masterAttempts).toBe(5);
    expect(await library.resolve(track.id)).toEqual({ playlistUrl, quality: "Normal" });
    expect(library.getSnapshot().tracks[0]).toEqual(
      expect.objectContaining({ state: "ready", quality: "Normal", requestedQuality: "High" }),
    );
  });

  it("takes the requested rendition as soon as the server finishes it", async () => {
    let masterAttempts = 0;
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      wait: async () => {},
      fetchMedia: async (url) => {
        const key = String(url);
        if (key === highMasterUrl) {
          return new Response(masterAttempts++ < 2 ? master : masterWithHigh);
        }
        if (key === highPlaylistUrl) return new Response(highPlaylist);
        if (key.startsWith(`/api/tracks/${track.id}/hls/high/`)) return new Response("high media");
        return media(completeResponses())(url);
      },
    });

    await library.download({ tracks: [track], quality: "High" });

    expect(masterAttempts).toBe(3);
    expect(await library.resolve(track.id)).toEqual({
      playlistUrl: highPlaylistUrl,
      quality: "High",
    });
  });

  it("does not download again when the server already settled for a lower tier", async () => {
    let online = true;
    const library = await createOfflineLibrary({
      userId: "listener",
      storage: new MemoryOfflineStorage(),
      wait: async () => {},
      fetchMedia: async (url) => {
        if (!online) throw new Error("the second request should not need the network");
        if (String(url) === highMasterUrl) return new Response(master);
        return media(completeResponses())(url);
      },
    });
    await library.download({ tracks: [track], quality: "High" });

    online = false;
    await library.download({ tracks: [track], quality: "High" });

    expect(await library.resolve(track.id)).toEqual({ playlistUrl, quality: "Normal" });
  });

  it("evicts the previous tier when a track is downloaded again at another quality", async () => {
    const storage = new MemoryOfflineStorage();
    const library = await createOfflineLibrary({
      userId: "listener",
      storage,
      wait: async () => {},
      fetchMedia: async (url) => {
        const key = String(url);
        if (key === highMasterUrl) return new Response(masterWithHigh);
        if (key === highPlaylistUrl) return new Response(highPlaylist);
        if (key.startsWith(`/api/tracks/${track.id}/hls/high/`)) return new Response("high media");
        return media(completeResponses())(url);
      },
    });

    await library.download({ tracks: [track], quality: "Normal" });
    await library.download({ tracks: [track], quality: "High" });

    expect(await library.resolve(track.id)).toEqual({
      playlistUrl: highPlaylistUrl,
      quality: "High",
    });
    // URL несёт тир, так что сегменты Normal уже никто не назовёт — их нельзя оставлять в кэше.
    expect(storage.storedUrls).toEqual([highPlaylistUrl, highInitUrl, highSegmentUrl]);
    expect(library.getSnapshot().tracks[0]).toEqual(
      expect.objectContaining({
        resourceUrls: [highPlaylistUrl, highInitUrl, highSegmentUrl],
      }),
    );
  });

  it("restores a browser-interrupted download as retryable", async () => {
    const storage = new MemoryOfflineStorage();
    await storage.save({
      userId: "listener",
      track,
      quality: "Normal",
      requestedQuality: "Normal",
      state: "downloading",
      playlistUrl,
      resourceUrls: [playlistUrl, initUrl],
      downloadedBytes: 10,
    });

    const library = await createOfflineLibrary({
      userId: "listener",
      storage,
      fetchMedia: media(completeResponses()),
    });

    expect(library.getSnapshot().tracks[0]).toEqual(
      expect.objectContaining({ state: "failed", error: "Download was interrupted." }),
    );
    await library.download({ tracks: [track], quality: "Normal" });
    expect(await library.resolve(track.id)).not.toBeNull();
  });
});
