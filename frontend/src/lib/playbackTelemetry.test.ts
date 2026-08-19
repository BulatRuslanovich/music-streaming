// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PlaybackEventInput } from "@/lib/events";
import { createListeningTracker, type ListeningTracker } from "@/lib/playbackTelemetry";
import type { Track } from "@/lib/types";

const song: Track = {
  id: "song",
  title: "Song",
  artistId: "artist",
  artistName: "Artist",
  durationSeconds: 210,
  originalFileName: "song.mp3",
  isFavorite: false,
  hasCover: false,
  hasLyrics: false,
  createdAt: "2026-01-01T00:00:00Z",
};

const origin = { source: "album", sourceId: "album-1" } as const;

let events: PlaybackEventInput[];
let tracker: ListeningTracker;

function typesOf(): string[] {
  return events.map((event) => event.type);
}

function lastOf(type: string): PlaybackEventInput {
  const found = events.filter((event) => event.type === type).at(-1);
  if (!found) throw new Error(`no ${type} event was recorded`);
  return found;
}

beforeEach(() => {
  events = [];
  tracker = createListeningTracker((event) => {
    events.push(event);
  });
});

describe("begin", () => {
  it("records the start with its origin", () => {
    tracker.begin(song, origin);

    expect(typesOf()).toEqual(["trackStarted"]);
    expect(lastOf("trackStarted")).toMatchObject({
      trackId: "song",
      durationSeconds: 210,
      source: "album",
      sourceId: "album-1",
    });
  });

  it("marks a repeat listen of the same track", () => {
    tracker.begin(song, {});
    tracker.finish("trackCompleted", {});
    tracker.begin(song, {});

    expect(typesOf()).toEqual(["trackStarted", "trackCompleted", "trackStarted", "trackReplayed"]);
  });

  it("does not mark a first listen of another track as a repeat", () => {
    tracker.begin(song, {});
    tracker.begin({ ...song, id: "other" }, {});

    expect(typesOf()).toEqual(["trackStarted", "trackStarted"]);
  });
});

describe("accumulate", () => {
  it("counts steady playback", () => {
    tracker.begin(song, {});
    for (let second = 1; second <= 40; second += 1) tracker.accumulate(second, {});

    expect(lastOf("trackPlayed")).toMatchObject({ listenedSeconds: 30, positionSeconds: 30 });
  });

  it("does not count a seek as listening", () => {
    tracker.begin(song, {});
    tracker.accumulate(1, {});
    tracker.accumulate(120, {});
    tracker.finish("trackSkipped", {});

    expect(lastOf("trackSkipped")).toMatchObject({ listenedSeconds: 1, positionSeconds: 120 });
  });

  it("does not count backwards movement", () => {
    tracker.begin(song, {});
    tracker.accumulate(1.5, {});
    tracker.accumulate(0, {});
    tracker.finish("trackSkipped", {});

    expect(lastOf("trackSkipped")).toMatchObject({ listenedSeconds: 1 });
  });

  it("beats once per thirty listened seconds", () => {
    tracker.begin(song, {});
    for (let second = 1; second <= 90; second += 1) tracker.accumulate(second, {});

    expect(typesOf().filter((type) => type === "trackPlayed")).toHaveLength(3);
  });

  it("stays quiet when no track is playing", () => {
    tracker.accumulate(10, {});

    expect(events).toHaveLength(0);
  });
});

describe("finish", () => {
  it("reports whole seconds", () => {
    tracker.begin(song, {});
    tracker.accumulate(1.75, {});
    tracker.finish("trackCompleted", {});

    expect(lastOf("trackCompleted")).toMatchObject({
      listenedSeconds: 1,
      positionSeconds: 1,
      durationSeconds: 210,
    });
  });

  it("reports at most once per track", () => {
    tracker.begin(song, {});
    tracker.finish("trackCompleted", {});
    tracker.finish("trackCompleted", {});

    expect(typesOf().filter((type) => type === "trackCompleted")).toHaveLength(1);
  });

  it("starts the next track from zero", () => {
    tracker.begin(song, {});
    for (let second = 1; second <= 50; second += 1) tracker.accumulate(second, {});
    tracker.finish("trackSkipped", {});

    tracker.begin({ ...song, id: "other" }, {});
    tracker.accumulate(1, {});
    tracker.finish("trackCompleted", {});

    expect(lastOf("trackCompleted")).toMatchObject({ trackId: "other", listenedSeconds: 1 });
  });
});

describe("pause", () => {
  it("reports the position reached so far", () => {
    tracker.begin(song, {});
    tracker.accumulate(1, {});
    tracker.pause(origin);

    expect(lastOf("trackPaused")).toMatchObject({
      trackId: "song",
      positionSeconds: 1,
      source: "album",
    });
  });

  it("keeps counting after a pause", () => {
    tracker.begin(song, {});
    tracker.accumulate(1, {});
    tracker.pause({});
    tracker.accumulate(2, {});
    tracker.finish("trackCompleted", {});

    expect(lastOf("trackCompleted")).toMatchObject({ listenedSeconds: 2 });
  });

  it("stays quiet when no track is playing", () => {
    tracker.pause({});

    expect(events).toHaveLength(0);
  });
});

describe("record", () => {
  it("sends every event through the injected recorder", () => {
    const record = vi.fn();
    const own = createListeningTracker(record);

    own.begin(song, {});
    own.accumulate(1, {});
    own.finish("trackCompleted", {});

    expect(record).toHaveBeenCalledTimes(2);
  });
});
