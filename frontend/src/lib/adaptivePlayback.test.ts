// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { adaptiveCap, choosePlaybackTransport } from "@/lib/adaptivePlayback";

describe("adaptive playback selection", () => {
  it("keeps a decodable original on the progressive stream", () => {
    expect(
      choosePlaybackTransport(
        {
          quality: "Original",
          progressiveTier: "Original",
          hlsEnabled: true,
          forceAdaptive: false,
        },
        { hlsJs: true, nativeHls: true },
      ),
    ).toBe("progressive");
  });

  it("uses hls.js first for quality tiers and degraded originals", () => {
    for (const quality of ["Low", "Normal", "High"] as const) {
      expect(
        choosePlaybackTransport(
          { quality, progressiveTier: quality, hlsEnabled: true, forceAdaptive: false },
          { hlsJs: true, nativeHls: true },
        ),
      ).toBe("hls.js");
    }

    expect(
      choosePlaybackTransport(
        {
          quality: "Original",
          progressiveTier: "Original",
          hlsEnabled: true,
          forceAdaptive: true,
        },
        { hlsJs: true, nativeHls: true },
      ),
    ).toBe("hls.js");
  });

  it("falls back through native HLS to progressive playback", () => {
    const request = {
      quality: "Normal" as const,
      progressiveTier: "Normal" as const,
      hlsEnabled: true,
      forceAdaptive: false,
    };

    expect(choosePlaybackTransport(request, { hlsJs: false, nativeHls: true })).toBe("native-hls");
    expect(choosePlaybackTransport(request, { hlsJs: false, nativeHls: false })).toBe(
      "progressive",
    );
    expect(
      choosePlaybackTransport({ ...request, hlsEnabled: false }, { hlsJs: true, nativeHls: true }),
    ).toBe("progressive");
  });

  it("caps an adaptive original at the high rendition", () => {
    expect(adaptiveCap("Original")).toBe("High");
    expect(adaptiveCap("Normal")).toBe("Normal");
  });
});
