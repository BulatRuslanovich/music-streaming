// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { tagLabel } from "./tagLabel";

describe("tagLabel", () => {
  it("capitalises the words a tag is made of", () => {
    expect(tagLabel("shoegaze")).toBe("Shoegaze");
    expect(tagLabel("witch house")).toBe("Witch House");
  });

  it("keeps a hyphenated genre spelled the way the genre spells itself", () => {
    expect(tagLabel("post-punk")).toBe("Post-punk");
    expect(tagLabel("hip-hop")).toBe("Hip-hop");
  });

  it("leaves connecting words lowercase unless they open the tag", () => {
    expect(tagLabel("drum and bass")).toBe("Drum and Bass");
    expect(tagLabel("the beat")).toBe("The Beat");
  });

  it("writes acronyms the way they are read", () => {
    expect(tagLabel("r&b")).toBe("R&B");
    expect(tagLabel("uk garage")).toBe("UK Garage");
  });

  it("survives what the provider might send", () => {
    expect(tagLabel("")).toBe("");
    expect(tagLabel("  spaced  out ")).toBe("Spaced Out");
    expect(tagLabel("ALREADY LOUD")).toBe("Already Loud");
  });
});
