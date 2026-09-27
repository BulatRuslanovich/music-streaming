// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { expect, seededTrack, test } from "./fixtures";

test.describe("what an admin can change", () => {
  test("an album can be renamed from its own page", async ({ signedIn: page }) => {
    await page.goto("/albums");
    await page
      .getByRole("link", { name: new RegExp(seededTrack.album, "i") })
      .first()
      .click();

    await page.getByRole("button", { name: "Edit" }).click();

    const renamed = `${seededTrack.album} Renamed`;
    const title = page.getByLabel("Title");

    await expect(title).toHaveValue(seededTrack.album);
    await title.fill(renamed);
    await page.getByRole("button", { name: "Save changes" }).click();

    await expect(page.getByRole("heading", { name: renamed })).toBeVisible();

    // Возвращаем как было, чтобы прогон не зависел от предыдущего.
    await page.getByRole("button", { name: "Edit" }).click();
    await page.getByLabel("Title").fill(seededTrack.album);
    await page.getByRole("button", { name: "Save changes" }).click();

    await expect(page.getByRole("heading", { name: seededTrack.album })).toBeVisible();
  });
});
