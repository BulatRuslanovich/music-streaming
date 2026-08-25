// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { expect, seededTrack, test } from "./fixtures";

test.describe("playback", () => {
  test("double-clicking a track starts it and the player picks it up", async ({
    signedIn: page,
  }) => {
    await page.goto("/tracks");

    const row = page.getByRole("row").filter({ hasText: seededTrack.title });
    await expect(row).toBeVisible();
    await row.dblclick();

    const audio = page.locator("audio");

    // Источник появляется, только когда трек действительно поставлен в проигрыватель.
    await expect
      .poll(() => audio.evaluate((element: HTMLAudioElement) => element.currentSrc), {
        timeout: 15_000,
      })
      .not.toBe("");

    await expect
      .poll(() => audio.evaluate((element: HTMLAudioElement) => element.currentTime), {
        timeout: 15_000,
      })
      .toBeGreaterThan(0);

    await expect(page.getByRole("button", { name: /^Pause/ }).first()).toBeVisible();
  });

  test("pausing stops the clock", async ({ signedIn: page }) => {
    await page.goto("/tracks");

    const row = page.getByRole("row").filter({ hasText: seededTrack.title });
    await row.dblclick();

    const audio = page.locator("audio");

    await expect
      .poll(() => audio.evaluate((element: HTMLAudioElement) => element.currentTime), {
        timeout: 15_000,
      })
      .toBeGreaterThan(0);

    await page
      .getByRole("button", { name: /^Pause/ })
      .first()
      .click();

    await expect
      .poll(() => audio.evaluate((element: HTMLAudioElement) => element.paused), {
        timeout: 5_000,
      })
      .toBe(true);
  });
});
