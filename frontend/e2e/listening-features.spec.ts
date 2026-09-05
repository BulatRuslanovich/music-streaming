// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { execFileSync } from "node:child_process";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { expect, owner, seededTrackRow, test } from "./fixtures";

test.setTimeout(90_000);

test("Connect controls another browser and transfers its playback", async ({
  signedIn: page,
  browser,
}) => {
  test.setTimeout(90_000);
  const row = await seededTrackRow(page);
  await row.dblclick();
  await page.getByRole("button", { name: /^Repeat:/ }).click();
  await page.getByRole("button", { name: /^Repeat:/ }).click();
  await expect
    .poll(() => page.locator("audio").evaluate((audio: HTMLAudioElement) => audio.currentTime))
    .toBeGreaterThan(1);
  const remoteContext = await browser.newContext({
    storageState: await page.context().storageState(),
  });
  try {
    const remote = await remoteContext.newPage();
    await remote.goto("/");
    await remote.getByRole("button", { name: "Caimack Connect", exact: true }).click();
    const dialog = remote.getByRole("dialog");
    await expect(dialog.locator("section")).toHaveCount(2);
    const source = dialog.locator("section").filter({ hasNotText: "This device" });
    await source.getByRole("button", { name: "Pause", exact: true }).click();
    await expect
      .poll(() => page.locator("audio").evaluate((audio: HTMLAudioElement) => audio.paused))
      .toBe(true);
    await source.getByRole("button", { name: "Play", exact: true }).click();
    await expect
      .poll(() => page.locator("audio").evaluate((audio: HTMLAudioElement) => audio.paused))
      .toBe(false);
    await expect(source.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    await source.getByRole("button", { name: "Continue here", exact: true }).click();
    await expect
      .poll(() => remote.locator("audio").evaluate((audio: HTMLAudioElement) => audio.paused))
      .toBe(false);
    await expect
      .poll(() => remote.locator("audio").evaluate((audio: HTMLAudioElement) => audio.currentTime))
      .toBeGreaterThan(1);
    await expect
      .poll(() => page.locator("audio").evaluate((audio: HTMLAudioElement) => audio.paused))
      .toBe(true);
  } finally {
    await remoteContext.close();
  }
});

for (const mode of ["gapless", "crossfade"] as const) {
  test(`${mode} advances prepared audio and pause stops the playback clock`, async ({
    page,
    request,
  }) => {
    test.setTimeout(60_000);
    (await request.post("/api/auth/login", { data: owner })).ok();
    const directory = await mkdtemp(join(tmpdir(), "caimack-transition-"));
    const tracks = [];
    try {
      for (const index of [1, 2]) {
        const existing = await request.get(
          `/api/tracks?q=${encodeURIComponent(`Transition ${mode} ${index}`)}`,
        );
        const items = (await existing.json()).items;
        if (items.length) {
          tracks.push(items[0]);
          continue;
        }
        const file = join(directory, `${index}.mp3`);
        execFileSync("ffmpeg", [
          "-hide_banner",
          "-loglevel",
          "error",
          "-f",
          "lavfi",
          "-i",
          `sine=frequency=${index * 220}:duration=12`,
          "-metadata",
          `title=Transition ${mode} ${index}`,
          "-metadata",
          "artist=Transition Test",
          "-metadata",
          `album=Transitions ${mode}`,
          "-c:a",
          "libmp3lame",
          "-b:a",
          "128k",
          file,
        ]);
        const response = await request.post("/api/tracks/upload", {
          headers: {
            "Content-Type": "audio/mpeg",
            "X-File-Name": encodeURIComponent(`${mode}-${index}.mp3`),
          },
          data: await readFile(file),
        });
        expect(response.ok(), await response.text()).toBe(true);
        const found = await request.get(
          `/api/tracks?q=${encodeURIComponent(`Transition ${mode} ${index}`)}`,
        );
        tracks.push((await found.json()).items[0]);
      }
      await page.context().addCookies((await request.storageState()).cookies);
      await page.addInitScript(
        ({ queue, transition }) => {
          localStorage.setItem(
            "caimack.sound",
            JSON.stringify({ transition, crossfadeSeconds: 3, normalization: "track" }),
          );
          localStorage.setItem(
            "music-streaming.player",
            JSON.stringify({ queue, index: 0, position: 0, repeat: "off", volume: 0.5 }),
          );
        },
        { queue: tracks, transition: mode },
      );
      await page.goto("/");
      await page
        .getByRole("contentinfo")
        .getByRole("button", { name: "Play", exact: true })
        .click();
      await expect
        .poll(() => page.locator("audio").getAttribute("data-buffered"), { timeout: 20_000 })
        .toBe("true");
      await expect
        .poll(() => page.locator("audio").getAttribute("data-track-id"), { timeout: 25_000 })
        .toBe(tracks[1].id);
      await page
        .getByRole("contentinfo")
        .getByRole("button", { name: "Pause", exact: true })
        .click();
      await expect(
        page.getByRole("contentinfo").getByRole("button", { name: "Play", exact: true }),
      ).toBeVisible();
      const seekbar = page
        .getByRole("contentinfo")
        .getByRole("slider", { name: "Seek within the track" });
      const pausedAt = await seekbar.inputValue();
      await page.waitForTimeout(1200);
      expect(await seekbar.inputValue()).toBe(pausedAt);
      await page.screenshot({ path: `/tmp/caimack-${mode}.png` });
    } finally {
      await rm(directory, { recursive: true, force: true });
    }
  });
}

test("monthly recap shows a story and exports a card", async ({ signedIn: page }) => {
  // UI fixture complements the real PostgreSQL month/discovery integration test.
  const response = await page.request.get("/api/tracks?pageSize=1");
  const track = (await response.json()).items[0];
  await page.route("**/api/me/recap?*", async (route) =>
    route.fulfill({
      json: {
        month: "2026-08",
        timeZone: "UTC",
        isComplete: true,
        listenedSeconds: 7200,
        plays: 30,
        uniqueTracks: 12,
        uniqueArtists: 4,
        previousListenedSeconds: 3600,
        topTracks: [{ track, listenedSeconds: 400, plays: 4 }],
        topArtists: [
          {
            id: track.artistId,
            name: "Massive Attack",
            listenedSeconds: 3000,
            plays: 12,
            hasImage: false,
          },
        ],
        discoveries: [],
        topGenre: "Trip hop",
        previousTopGenre: "Rock",
      },
    }),
  );
  await page.route("**/api/me/recap", async (route) =>
    route.fulfill({
      json: {
        month: "2026-08",
        timeZone: "UTC",
        isComplete: true,
        listenedSeconds: 7200,
        plays: 30,
        uniqueTracks: 12,
        uniqueArtists: 4,
        previousListenedSeconds: 3600,
        topTracks: [{ track, listenedSeconds: 400, plays: 4 }],
        topArtists: [
          {
            id: track.artistId,
            name: "Massive Attack",
            listenedSeconds: 3000,
            plays: 12,
            hasImage: false,
          },
        ],
        discoveries: [],
        topGenre: "Trip hop",
        previousTopGenre: "Rock",
      },
    }),
  );
  await page.goto("/statistics");
  await page.getByRole("link", { name: "Monthly recap" }).click();
  await expect(page.getByRole("heading", { name: "120 minutes of music" })).toBeVisible();
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Massive Attack" })).toBeVisible();
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Save image", exact: true }).click();
  expect((await download).suggestedFilename()).toBe("caimack-2026-08.png");
  await page.screenshot({ path: "/tmp/caimack-recap.png", fullPage: true });
});
