// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { mergeRadioBatch, validRadioSession } from "./radioSession";
import type { RecommendedTrack, Track } from "../types";

const track = (id: string): Track => ({
  id,
  title: id,
  artistId: "artist",
  artistName: "Artist",
  durationSeconds: 180,
  originalFileName: `${id}.mp3`,
  isFavorite: false,
  hasCover: false,
  hasLyrics: false,
  createdAt: "2026-01-01T00:00:00Z",
});

const recommended = (id: string): RecommendedTrack => ({
  track: track(id),
  reason: { kind: "discovery" },
});

describe("radio session state", () => {
  it("drops tracks already present while retaining reasons", () => {
    const merged = mergeRadioBatch([track("known")], { known: { kind: "soundsLike" } }, {}, [
      recommended("known"),
      recommended("fresh"),
    ]);

    expect(merged.tracks.map((item) => item.id)).toEqual(["fresh"]);
    expect(merged.reasons).toEqual({
      known: { kind: "soundsLike" },
      fresh: { kind: "discovery" },
    });
  });

  it("carries queue signals only for the tracks that have them", () => {
    const explored: RecommendedTrack = {
      ...recommended("fresh"),
      signals: { explore: true },
    };

    const merged = mergeRadioBatch([], {}, {}, [recommended("plain"), explored]);

    expect(Object.keys(merged.signals)).toEqual(["fresh"]);
    expect(merged.signals.fresh.explore).toBe(true);
  });

  it("accepts persisted radio state and rejects malformed values", () => {
    expect(validRadioSession({ seedTrackId: "seed", reasons: {} })).toBe(true);
    expect(validRadioSession(undefined)).toBe(false);
    expect(validRadioSession({ seedTrackId: "seed" })).toBe(false);
  });
});
