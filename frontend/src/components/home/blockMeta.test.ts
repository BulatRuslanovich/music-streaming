// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import type { Translate } from "@/contexts/I18nContext";
import type { HomeBlock, Track } from "@/lib/types";
import { MOSAIC_POOL, blockEyebrow, mosaicPool, splitMobileTail } from "./blockMeta";

function block(
  baseKey: string,
  tracks: Track[] = [],
  zone: HomeBlock["zone"] = "Browse",
): HomeBlock {
  return { key: baseKey, baseKey, layout: "Shelf", zone, tracks };
}

/** Ключ и подставленный субъект видно как есть — тест про выбор строки, а не про словарь. */
const t: Translate = (key, values) =>
  values?.subject ? `${key}:${String(values.subject)}` : String(key);

function explained(baseKey: string, kind: string, subject?: string): HomeBlock {
  return { ...block(baseKey), reason: { kind, subject: subject ?? null, subjectId: null } };
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

  it("splits the order HomeFeedService actually sends the same way as before", () => {
    // Порядок зоны Browse в HomeFeedService переставлен ради чередования макетов. Он двигает
    // вторую рекомендательную полку через два блока, и хвост обязан остаться прежним — иначе
    // «Показать ещё» на телефоне начнёт прятать другое.
    const { head, tail } = splitMobileTail([
      block("newArrivals"),
      block("forYou"),
      block("topTracks"),
      block("newAlbums"),
      block("artistsForYou"),
      block("discover"),
      block("yourPlaylists"),
    ]);

    expect(head.map((item) => item.baseKey)).toEqual(["newArrivals", "forYou", "topTracks"]);
    expect(tail.map((item) => item.baseKey)).toEqual([
      "newAlbums",
      "artistsForYou",
      "discover",
      "yourPlaylists",
    ]);
  });
});

describe("blockEyebrow", () => {
  it("explains the shelf whose title says nothing about why it is there", () => {
    expect(blockEyebrow(explained("forYou", "becauseYouListened", "Boards of Canada"), t)).toBe(
      "rec.reason.becauseYouListened:Boards of Canada",
    );
  });

  it("stays silent when the title already is the reason", () => {
    expect(
      blockEyebrow(explained("becauseYouListened", "becauseYouListened", "Aphex"), t),
    ).toBeUndefined();
    expect(blockEyebrow(explained("similarTo", "similarTo", "Autechre"), t)).toBeUndefined();
    expect(blockEyebrow(explained("genreMix", "fromGenreYouLike", "IDM"), t)).toBeUndefined();
  });

  it("stays silent when the reason restates the title", () => {
    expect(blockEyebrow(explained("popular", "trending"), t)).toBeUndefined();
    expect(blockEyebrow(explained("newReleases", "freshInLibrary"), t)).toBeUndefined();
    expect(blockEyebrow(explained("discover", "discovery"), t)).toBeUndefined();
    expect(blockEyebrow(explained("continueListening", "continueListening"), t)).toBeUndefined();
  });

  it("names the period of the chart, which nothing else on the page does", () => {
    expect(blockEyebrow(block("topTracks"), t)).toBe("home.topPeriod");
  });

  it("has nothing to say about a block that carries no reason", () => {
    expect(blockEyebrow(block("forYou"), t)).toBeUndefined();
    expect(blockEyebrow(block("newAlbums"), t)).toBeUndefined();
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
