// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { describe, expect, it } from "vitest";
import { userAfterMeFailure } from "@/lib/authBootstrap";
import type { User } from "@/lib/types";

const cachedUser = {
  id: "11111111-1111-1111-1111-111111111111",
  username: "listener",
  displayName: "Listener",
  isAdmin: false,
} as User;

describe("auth bootstrap", () => {
  it("keeps a cached session hint when the browser is offline", () => {
    expect(userAfterMeFailure(cachedUser, false)).toBe(cachedUser);
  });

  it("clears a cached session hint when the server is reachable", () => {
    expect(userAfterMeFailure(cachedUser, true)).toBeNull();
  });

  it("cannot invent a user when there is no cached session hint", () => {
    expect(userAfterMeFailure(null, false)).toBeNull();
  });
});
