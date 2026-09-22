// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { sha256File } from "./fileHash";

describe("sha256File", () => {
  it("hashes a blob incrementally", async () => {
    const blob = new Blob(["abc"]);

    expect(await sha256File(blob)).toBe(
      "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
    );
  });

  it("handles an empty blob", async () => {
    expect(await sha256File(new Blob())).toBe(
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    );
  });
});
