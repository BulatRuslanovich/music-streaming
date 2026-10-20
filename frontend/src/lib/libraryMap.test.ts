// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import {
  MAX_ZOOM,
  closestToCentre,
  constellation,
  fitView,
  playsLevel,
  pointInPolygon,
  soundPath,
  toMap,
  toScreen,
  zoomAt,
} from "@/lib/libraryMap";

describe("map view", () => {
  it("fits the unit square into the middle of a wide canvas", () => {
    const view = fitView(800, 400);

    expect(toScreen({ x: 0, y: 0 }, view, 400)).toEqual({ x: 200, y: 0 });
    expect(toScreen({ x: 1, y: 1 }, view, 400)).toEqual({ x: 600, y: 400 });
  });

  it("keeps the spot under the cursor in place while zooming", () => {
    const view = fitView(500, 500);
    const cursor = { x: 120, y: 340 };
    const under = toMap(cursor, view, 500);

    const zoomed = zoomAt(view, 2.5, cursor);

    expect(zoomed.k).toBe(2.5);
    const after = toScreen(under, zoomed, 500);
    expect(after.x).toBeCloseTo(cursor.x);
    expect(after.y).toBeCloseTo(cursor.y);
  });

  it("fills the canvas with the tracks themselves when their bounds are known", () => {
    const view = fitView(1000, 500, { minX: 0.4, minY: 0.4, maxX: 0.6, maxY: 0.5 });

    const middle = toScreen({ x: 0.5, y: 0.45 }, view, 500);
    expect(middle.x).toBeCloseTo(500);
    expect(middle.y).toBeCloseTo(250);

    const top = toScreen({ x: 0.5, y: 0.4 }, view, 500);
    expect(top.y).toBeGreaterThanOrEqual(40);
    expect(top.y).toBeLessThan(80);
  });

  it("stops zooming at the limit", () => {
    expect(zoomAt(fitView(100, 100), 1000, { x: 0, y: 0 }).k).toBe(MAX_ZOOM);
  });
});

describe("pointInPolygon", () => {
  const square = [
    { x: 0, y: 0 },
    { x: 1, y: 0 },
    { x: 1, y: 1 },
    { x: 0, y: 1 },
  ];

  it("tells inside from outside", () => {
    expect(pointInPolygon({ x: 0.5, y: 0.5 }, square)).toBe(true);
    expect(pointInPolygon({ x: 1.5, y: 0.5 }, square)).toBe(false);
  });
});

describe("soundPath", () => {
  it("walks from the left edge to each nearest neighbour in turn", () => {
    const spots = [
      { id: "c", x: 0.9, y: 0 },
      { id: "a", x: 0, y: 0 },
      { id: "d", x: 0.1, y: 0.9 },
      { id: "b", x: 0.1, y: 0.1 },
    ];

    expect(soundPath(spots).map((spot) => spot.id)).toEqual(["a", "b", "d", "c"]);
  });
});

describe("closestToCentre", () => {
  it("keeps the middle of a large selection", () => {
    const spots = [0, 0.4, 0.5, 0.6, 1].map((x) => ({ x, y: 0 }));

    expect(
      closestToCentre(spots, 3)
        .map((spot) => spot.x)
        .sort(),
    ).toEqual([0.4, 0.5, 0.6]);
  });
});

describe("playsLevel", () => {
  it("rises fast for the first plays and saturates at the most played", () => {
    expect(playsLevel(0, 50)).toBe(0);
    expect(playsLevel(50, 50)).toBe(1);
    expect(playsLevel(5, 50)).toBeGreaterThan(0.4);
  });
});

describe("constellation", () => {
  it("links neighbours inside a cluster but not across the gap between clusters", () => {
    const spots = [
      { x: 0.1, y: 0.1 },
      { x: 0.12, y: 0.1 },
      { x: 0.11, y: 0.12 },
      { x: 0.9, y: 0.9 },
      { x: 0.92, y: 0.9 },
      { x: 0.91, y: 0.92 },
    ];

    const links = constellation(spots);

    expect(links.length).toBeGreaterThan(0);
    for (const [a, b] of links) expect(a < 3).toBe(b < 3);
  });

  it("lists every pair once", () => {
    const spots = [
      { x: 0, y: 0 },
      { x: 0.01, y: 0 },
    ];

    expect(constellation(spots)).toEqual([[0, 1]]);
  });
});
