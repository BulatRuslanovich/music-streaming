// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import type { Translate } from "@/contexts/I18nContext";
import type { HomeBlock } from "@/lib/types";
import { blockNote } from "./blockMeta";

function block(baseKey: string, zone: HomeBlock["zone"] = "Browse"): HomeBlock {
  return { key: baseKey, baseKey, layout: "Shelf", zone, tracks: [] };
}

const t: Translate = (key, values) =>
  values?.subject ? `${key}:${String(values.subject)}` : String(key);

function explained(baseKey: string, kind: string, subject?: string): HomeBlock {
  return { ...block(baseKey), reason: { kind, subject: subject ?? null, subjectId: null } };
}

describe("blockNote", () => {
  it("explains the shelf whose title says nothing about why it is there", () => {
    expect(blockNote(explained("forYou", "becauseYouListened", "Boards of Canada"), t)).toBe(
      "rec.reason.becauseYouListened:Boards of Canada",
    );
  });

  it("stays silent when the title already is the reason", () => {
    expect(
      blockNote(explained("becauseYouListened", "becauseYouListened", "Aphex"), t),
    ).toBeUndefined();
  });

  it("stays silent when the reason restates the title", () => {
    expect(blockNote(explained("discover", "discovery"), t)).toBeUndefined();
  });

  it("names the period of the chart, which nothing else on the page does", () => {
    expect(blockNote(block("topTracks"), t)).toBe("home.topPeriod");
  });

  it("has nothing to say about a block that carries no reason", () => {
    expect(blockNote(block("forYou"), t)).toBeUndefined();
    expect(blockNote(block("newAlbums"), t)).toBeUndefined();
  });
});
