// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import {
  parseDirection,
  parseListenerSort,
  parsePeriod,
  parseSource,
  percent,
  uploaderLabel,
  uploadPoints,
} from "@/lib/adminStatistics";
import type { AdminUpload, DailyUpload, IngestionSource } from "@/lib/types";

function upload(
  ingestionSource: IngestionSource,
  addedByUsername: string | null,
): Pick<AdminUpload, "addedByUsername" | "ingestionSource"> {
  return { ingestionSource, addedByUsername };
}

function day(date: string, tracks: number): DailyUpload {
  return { date, tracks, bytes: tracks * 1000 };
}

describe("uploaderLabel", () => {
  it("names the person who sent the file", () => {
    expect(uploaderLabel(upload("WebUpload", "bulat"))).toEqual({
      kind: "user",
      username: "bulat",
    });
  });

  it("calls a directory import a system import rather than an unknown person", () => {
    expect(uploaderLabel(upload("DirectoryImport", null))).toEqual({ kind: "system" });
  });

  it("keeps tracks from before the signature existed separate from the system import", () => {
    expect(uploaderLabel(upload("Unknown", null))).toEqual({ kind: "unknown" });
  });

  // Импорт по устройству безымянный, но если имя всё же приехало — показываем человека.
  it("prefers a name over the source when both are present", () => {
    expect(uploaderLabel(upload("DirectoryImport", "bulat"))).toEqual({
      kind: "user",
      username: "bulat",
    });
  });
});

describe("uploadPoints", () => {
  it("counts tracks rather than seconds", () => {
    const points = uploadPoints([day("2026-09-01", 3), day("2026-09-02", 0)], (iso) => iso);

    expect(points.map((p) => p.value)).toEqual([3, 0]);
  });

  it("labels about five points however long the period is", () => {
    const days = Array.from({ length: 40 }, (_, i) => day(`2026-09-${String(i + 1)}`, i));
    const ticked = uploadPoints(days, (iso) => iso).filter((p) => p.tick !== undefined);

    expect(ticked.length).toBeLessThanOrEqual(6);
    expect(ticked.length).toBeGreaterThanOrEqual(4);
  });

  it("has nothing to draw for an empty period", () => {
    expect(uploadPoints([], (iso) => iso)).toEqual([]);
  });
});

describe("query string parsing", () => {
  it("keeps a value the API knows", () => {
    expect(parsePeriod("Quarter", "Month")).toBe("Quarter");
    expect(parseListenerSort("UploadedBytes", "ListenedSeconds")).toBe("UploadedBytes");
    expect(parseDirection("Asc", "Desc")).toBe("Asc");
  });

  // Адрес правит кто угодно, а значение едет в API как enum: мусор туда попасть не должен.
  it("falls back rather than passing junk from the address bar to the server", () => {
    expect(parsePeriod("Century", "Month")).toBe("Month");
    expect(parsePeriod(null, "Month")).toBe("Month");
    expect(parseListenerSort("passwordHash", "ListenedSeconds")).toBe("ListenedSeconds");
    expect(parseDirection("sideways", "Desc")).toBe("Desc");
  });

  it("treats a missing source as no filter at all, not as the unknown source", () => {
    expect(parseSource(null)).toBeUndefined();
    expect(parseSource("")).toBeUndefined();
    expect(parseSource("Unknown")).toBe("Unknown");
    expect(parseSource("WebUpload")).toBe("WebUpload");
  });
});

describe("percent", () => {
  it("turns a share into a whole percentage", () => {
    expect(percent(0)).toBe(0);
    expect(percent(0.756)).toBe(76);
    expect(percent(1)).toBe(100);
  });
});
