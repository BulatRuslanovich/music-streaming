// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { expect, seededTrack, seededTrackRow, test } from "./fixtures";

test.describe("browsing the library", () => {
  test("the seeded track is listed with its artist and album", async ({ signedIn: page }) => {
    const row = await seededTrackRow(page);

    await expect(row).toContainText(seededTrack.artist);
  });

  test("search finds the track by title", async ({ signedIn: page }) => {
    await page.goto(`/search?q=${encodeURIComponent(seededTrack.title)}`);

    await expect(page.getByText(seededTrack.title).first()).toBeVisible();
  });

  test("search for something that is not there says so instead of failing", async ({
    signedIn: page,
  }) => {
    await page.goto("/search?q=zzzzzznothinghere");

    await expect(page.getByText(seededTrack.title)).toHaveCount(0);
    await expect(page.locator("body")).not.toContainText(/unhandled|exception/i);
  });

  test("the album page lists the tracks of that album", async ({ signedIn: page }) => {
    await page.goto("/albums");
    await page
      .getByRole("link", { name: new RegExp(seededTrack.album, "i") })
      .first()
      .click();

    await expect(page.getByRole("heading", { name: seededTrack.album })).toBeVisible();
    await expect(page.getByRole("row").filter({ hasText: seededTrack.title })).toBeVisible();
  });
});
