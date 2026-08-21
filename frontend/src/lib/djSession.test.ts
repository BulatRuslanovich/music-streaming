// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { defaultDjVariety, mergeDjBatch, validDjSession } from "./djSession";
import type { RecommendedTrack, Track } from "./types";

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

describe("DJ session state", () => {
  it("starts discovery in adventurous mode and other sessions balanced", () => {
    expect(defaultDjVariety("Discover")).toBe("Adventurous");
    expect(defaultDjVariety("ForYou")).toBe("Balanced");
    expect(defaultDjVariety("Rediscover")).toBe("Balanced");
    expect(defaultDjVariety("Flow")).toBe("Balanced");
  });

  it("drops tracks already present while retaining reasons", () => {
    const merged = mergeDjBatch([track("known")], { known: { kind: "rediscovery" } }, [
      recommended("known"),
      recommended("fresh"),
    ]);

    expect(merged.tracks.map((item) => item.id)).toEqual(["fresh"]);
    expect(merged.reasons).toEqual({
      known: { kind: "rediscovery" },
      fresh: { kind: "discovery" },
    });
  });

  it("accepts persisted DJ state and rejects old or malformed values", () => {
    expect(
      validDjSession({
        mode: "ForYou",
        variety: "Balanced",
        status: "idle",
        reasons: {},
      }),
    ).toBe(true);
    expect(validDjSession(undefined)).toBe(false);
    expect(validDjSession({ mode: "ForYou" })).toBe(false);
  });
});
