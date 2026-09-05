// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { recapSlides, type RecapSlideKind } from "./recapStory";
import type { MonthlyRecap } from "./recap";
import type { StatisticsEntry, StatisticsTrack } from "./types";

function track(id: string, title: string): StatisticsTrack {
  return {
    track: {
      id,
      title,
      artistId: "artist",
      artistName: "Artist",
      durationSeconds: 200,
      originalFileName: `${id}.flac`,
      isFavorite: false,
      hasCover: true,
      hasLyrics: false,
      createdAt: "2026-08-01T00:00:00Z",
    },
    listenedSeconds: 600,
    plays: 3,
  };
}

function entry(id: string, name: string): StatisticsEntry {
  return { id, name, listenedSeconds: 900, plays: 4, hasImage: true };
}

function recap(overrides: Partial<MonthlyRecap> = {}): MonthlyRecap {
  return {
    month: "2026-08",
    timeZone: "UTC",
    listenedSeconds: 7200,
    plays: 30,
    uniqueTracks: 12,
    uniqueArtists: 4,
    previousListenedSeconds: 3600,
    topTracks: [track("a", "First"), track("b", "Second")],
    topArtists: [entry("artist", "Massive Attack")],
    discoveries: [entry("new", "Boards of Canada")],
    topGenre: "Trip hop",
    previousTopGenre: "Rock",
    ...overrides,
  };
}

const kinds = (data: MonthlyRecap): RecapSlideKind[] =>
  recapSlides(data).map((slide) => slide.kind);

describe("recap story", () => {
  it("tells the full month in a fixed order", () => {
    expect(kinds(recap())).toEqual([
      "intro",
      "time",
      "topTrack",
      "topTracks",
      "topArtist",
      "discoveries",
      "genre",
      "finale",
    ]);
  });

  it("keeps only the opening and the ending for a month with nothing in it", () => {
    const quiet = recap({ topTracks: [], topArtists: [], discoveries: [], topGenre: null });

    expect(kinds(quiet)).toEqual(["intro", "time", "finale"]);
  });

  it("does not repeat the leader as a list of one", () => {
    expect(kinds(recap({ topTracks: [track("a", "Alone")] }))).not.toContain("topTracks");
  });

  it("names the previous genre only when it really changed", () => {
    const shifted = recapSlides(recap()).find((slide) => slide.kind === "genre");
    expect(shifted).toEqual({ kind: "genre", genre: "Trip hop", previous: "Rock" });

    const same = recapSlides(recap({ previousTopGenre: "Trip hop" })).find(
      (slide) => slide.kind === "genre",
    );
    expect(same).toEqual({ kind: "genre", genre: "Trip hop", previous: null });
  });

  it("survives a genre that never arrived from the API", () => {
    // `WhenWritingNull` на бэкенде выкидывает пустые поля из JSON, поэтому здесь undefined,
    // а не null. Раньше это протекало в перевод и печаталось как «{from}».
    const missing = recapSlides(recap({ previousTopGenre: undefined }));
    expect(missing.find((slide) => slide.kind === "genre")).toEqual({
      kind: "genre",
      genre: "Trip hop",
      previous: null,
    });

    expect(kinds(recap({ topGenre: undefined }))).not.toContain("genre");
  });

  it("reports the change against the previous month, and nothing when there is none", () => {
    const withChange = recapSlides(recap()).find((slide) => slide.kind === "time");
    expect(withChange).toEqual({ kind: "time", changePercent: 100 });

    const withoutBaseline = recapSlides(recap({ previousListenedSeconds: 0 })).find(
      (slide) => slide.kind === "time",
    );
    expect(withoutBaseline).toEqual({ kind: "time", changePercent: null });
  });
});
