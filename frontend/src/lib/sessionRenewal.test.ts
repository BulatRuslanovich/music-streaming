// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { isStale, renewalIntervalMs } from "./sessionRenewal";

describe("renewalIntervalMs", () => {
  it("renews well before the token expires", () => {
    const lifetime = 10 * 60_000;

    expect(renewalIntervalMs(10)).toBeLessThan(lifetime);
    expect(renewalIntervalMs(10)).toBe(400_000);
  });

  it("does not renew more often than every half minute", () => {
    expect(renewalIntervalMs(0.1)).toBe(30_000);
  });

  it("falls back to the shortest interval on a nonsense lifetime", () => {
    for (const lifetime of [0, -5, Number.NaN, Number.POSITIVE_INFINITY]) {
      expect(renewalIntervalMs(lifetime)).toBe(30_000);
    }
  });

  it("caps the interval so a huge lifetime still renews within the hour", () => {
    expect(renewalIntervalMs(24 * 60)).toBe(60 * 60_000);
  });
});

describe("isStale", () => {
  it("is stale once a whole interval has passed", () => {
    expect(isStale(0, 400_000, 400_000)).toBe(true);
    expect(isStale(0, 399_999, 400_000)).toBe(false);
  });

  it("catches a tab that was throttled in the background", () => {
    const interval = renewalIntervalMs(10);

    expect(isStale(0, 45 * 60_000, interval)).toBe(true);
  });
});
