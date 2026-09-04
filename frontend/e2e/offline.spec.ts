// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { expect, seededTrack, seededTrackRow, test } from "./fixtures";

test.describe("offline downloads", () => {
  test("downloads a track, reloads offline, and plays the local HLS copy", async ({
    signedIn: page,
  }) => {
    test.setTimeout(180_000);

    let row = await seededTrackRow(page);
    await page.evaluate(async () => {
      await navigator.serviceWorker.ready;
    });
    await expect
      .poll(() =>
        page.evaluate(async () => {
          const cache = await caches.open("caimack-shell-v1");
          return Boolean(await cache.match("/", { ignoreVary: true }));
        }),
      )
      .toBe(true);

    await row.getByRole("button", { name: `More actions for ${seededTrack.title}` }).click();
    await page.getByRole("menuitem", { name: "Download for offline listening" }).click();
    await expect(page.getByText("Track is ready for offline listening.")).toBeVisible({
      timeout: 150_000,
    });

    await expect
      .poll(() =>
        page.evaluate(async () => {
          const cache = await caches.open("caimack-offline-media-v1");
          return (await cache.keys()).length;
        }),
      )
      .toBeGreaterThan(1);

    await page.context().setOffline(true);
    await page.reload({ waitUntil: "domcontentloaded" });

    await page.getByPlaceholder("Filter tracks").fill(seededTrack.title);
    row = page.getByRole("row").filter({ hasText: seededTrack.title });
    await expect(row).toBeVisible({ timeout: 15_000 });
    await row.dblclick();

    const audio = page.locator("audio");
    await expect
      .poll(() => audio.evaluate((element: HTMLAudioElement) => element.currentTime), {
        timeout: 20_000,
      })
      .toBeGreaterThan(0);
  });
});
