// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { isMixSlug } from "@/lib/mixes";

describe("mix routes", () => {
  it("recognizes only supported mix slugs", () => {
    expect(["daily", "new", "top"].every(isMixSlug)).toBe(true);
    expect(isMixSlug("weekly")).toBe(false);
    expect(isMixSlug("__proto__")).toBe(false);
  });
});
