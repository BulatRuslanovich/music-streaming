// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import type { HomeBlock, Track } from "@/lib/types";
import { MOSAIC_POOL, mosaicPool, splitMobileTail } from "./blockMeta";

function block(
  baseKey: string,
  tracks: Track[] = [],
  zone: HomeBlock["zone"] = "Browse",
): HomeBlock {
  return { key: baseKey, baseKey, layout: "Shelf", zone, tracks };
}

function track(id: string): Track {
  return {
    id,
    title: id,
    durationSeconds: 100,
    artists: [],
    hasCover: false,
    hasLyrics: false,
    createdAt: "2026-01-01T00:00:00Z",
  } as unknown as Track;
}

describe("splitMobileTail", () => {
  it("keeps the first recommendation shelf and sends the second to the tail", () => {
    const { head, tail } = splitMobileTail([
      block("newArrivals"),
      block("forYou"),
      block("topTracks"),
      block("discover"),
    ]);

    expect(head.map((item) => item.baseKey)).toEqual(["newArrivals", "forYou", "topTracks"]);
    expect(tail.map((item) => item.baseKey)).toEqual(["discover"]);
  });

  it("sends the blocks that have their own nav destination to the tail", () => {
    const { head, tail } = splitMobileTail([
      block("newAlbums"),
      block("artistsForYou"),
      block("yourPlaylists"),
    ]);

    expect(head).toEqual([]);
    expect(tail.map((item) => item.baseKey)).toEqual([
      "newAlbums",
      "artistsForYou",
      "yourPlaylists",
    ]);
  });

  it("does not let artistsForYou consume a recommendation slot", () => {
    // Он приезжает рекомендацией, но в хвост попадает по имени — иначе он бы съел позицию,
    // и единственная полка «для вас» уехала бы вниз вместе с ним.
    const { head, tail } = splitMobileTail([
      block("artistsForYou"),
      block("forYou"),
      block("albumsForYou"),
    ]);

    expect(head.map((item) => item.baseKey)).toEqual(["forYou"]);
    expect(tail.map((item) => item.baseKey)).toEqual(["artistsForYou", "albumsForYou"]);
  });

  it("preserves the backend order within each part", () => {
    const browse = [block("newArrivals"), block("newAlbums"), block("forYou"), block("topTracks")];

    const { head, tail } = splitMobileTail(browse);

    expect(head.map((item) => item.baseKey)).toEqual(["newArrivals", "forYou", "topTracks"]);
    expect(tail.map((item) => item.baseKey)).toEqual(["newAlbums"]);
  });
});

describe("mosaicPool", () => {
  it("stops at sixteen distinct tracks", () => {
    const many = Array.from({ length: 40 }, (_, index) => track(`t${index}`));

    expect(mosaicPool([block("forYou", many)])).toHaveLength(MOSAIC_POOL);
  });

  it("skips the hero so the tiles do not mirror the block right above them", () => {
    const pool = mosaicPool([
      block("dailyMix", [track("hero")], "Lead"),
      block("forYou", [track("a")]),
    ]);

    expect(pool.map((item) => item.id)).toEqual(["a"]);
  });

  it("dedupes across blocks", () => {
    const pool = mosaicPool([
      block("forYou", [track("a"), track("b")]),
      block("discover", [track("b"), track("c")]),
    ]);

    expect(pool.map((item) => item.id)).toEqual(["a", "b", "c"]);
  });

  it("returns what it has when the feed is short", () => {
    expect(mosaicPool([block("forYou", [track("a")])])).toHaveLength(1);
  });
});
