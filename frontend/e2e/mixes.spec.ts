// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { expect, test } from "./fixtures";

test.describe("mixes", () => {
  test("daily mix renders without a Server Components error", async ({ signedIn: page }) => {
    await page.goto("/mixes/daily");

    await expect(page.getByRole("heading", { name: "Daily mix" })).toBeVisible();
  });
});
