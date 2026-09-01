// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";

/**
 * Подрезка главной на телефоне живёт целиком в CSS (`max-md:` + nth-child), поэтому vitest её не
 * видит — этот спек и есть вся страховочная сетка.
 *
 * Лента подменяется на синтетическую вместо того, чтобы полагаться на засеянную библиотеку:
 * глобальный setup кладёт один трек, а половина блоков требует минимум четырёх и просто не
 * приезжает. Проверяем ровно раскладку, а не то, сколько музыки нашлось на инстансе.
 */
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

const SHELF_SIZE = 12;

function tracks(prefix: string, count = SHELF_SIZE) {
  return Array.from({ length: count }, (_, index) => ({
    id: `${prefix}-track-${index}`,
    title: `${prefix} track ${index}`,
    artistId: `${prefix}-artist`,
    artistName: `${prefix} artist`,
    artists: [{ id: `${prefix}-artist`, name: `${prefix} artist` }],
    albumId: `${prefix}-album-${index}`,
    albumTitle: `${prefix} album ${index}`,
    durationSeconds: 180,
    originalFileName: `${prefix}-${index}.mp3`,
    isFavorite: false,
    hasCover: false,
    hasLyrics: false,
    createdAt: "2026-01-01T00:00:00Z",
  }));
}

function albums(prefix: string, count = SHELF_SIZE) {
  return Array.from({ length: count }, (_, index) => ({
    id: `${prefix}-album-${index}`,
    title: `${prefix} album ${index}`,
    artistId: `${prefix}-artist`,
    artistName: `${prefix} artist`,
    trackCount: 10,
    durationSeconds: 1800,
    hasCover: false,
    createdAt: "2026-01-01T00:00:00Z",
  }));
}

function artists(count = SHELF_SIZE) {
  return Array.from({ length: count }, (_, index) => ({
    id: `artist-${index}`,
    name: `Artist ${index}`,
    albumCount: 2,
    trackCount: 20,
    hasImage: false,
  }));
}

function playlists(count = SHELF_SIZE) {
  return Array.from({ length: count }, (_, index) => ({
    id: `playlist-${index}`,
    name: `Playlist ${index}`,
    isPublic: false,
    ownerId: "owner",
    ownerName: "owner",
    trackCount: 10,
    durationSeconds: 1800,
    hasCover: false,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  }));
}

const feed = {
  blocks: [
    {
      key: "dailyMix",
      baseKey: "dailyMix",
      layout: "Hero",
      zone: "Lead",
      tracks: tracks("mix", 20),
      totalCount: 60,
    },
    {
      key: "quickTiles",
      baseKey: "quickTiles",
      layout: "QuickTiles",
      zone: "Quick",
      tracks: tracks("recent", 5),
      playlists: playlists(2),
    },
    {
      key: "newArrivals",
      baseKey: "newArrivals",
      layout: "Grid",
      zone: "Browse",
      tracks: tracks("fresh"),
    },
    {
      key: "forYou",
      baseKey: "forYou",
      layout: "Shelf",
      zone: "Browse",
      reason: { kind: "ForYou" },
      tracks: tracks("foryou"),
    },
    {
      key: "topTracks",
      baseKey: "topTracks",
      layout: "Chart",
      zone: "Browse",
      tracks: tracks("top"),
    },
    {
      key: "discover",
      baseKey: "discover",
      layout: "Shelf",
      zone: "Browse",
      reason: { kind: "Discover" },
      tracks: tracks("discover"),
    },
    {
      key: "newAlbums",
      baseKey: "newAlbums",
      layout: "Shelf",
      zone: "Browse",
      albums: albums("new"),
    },
    {
      key: "artistsForYou",
      baseKey: "artistsForYou",
      layout: "Circles",
      zone: "Browse",
      artists: artists(),
    },
    {
      key: "yourPlaylists",
      baseKey: "yourPlaylists",
      layout: "Shelf",
      zone: "Browse",
      playlists: playlists(),
    },
  ],
  stats: {
    trackCount: 500,
    albumCount: 50,
    totalDurationSeconds: 90000,
    totalBytes: 1_000_000_000,
    favoriteCount: 30,
  },
  isColdStart: false,
};

/**
 * Перехват ставится до перехода: ленту запрашивает ещё и предзагрузка из `<head>`, до того как
 * загрузится React.
 */
async function openStubbedHome(page: Page) {
  await page.route("**/api/home/feed*", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(feed) }),
  );

  await page.goto("/");
  await expect(page.getByRole("heading", { name: /Daily mix/i })).toBeVisible();
}

function section(page: Page, name: RegExp) {
  return page.locator("section").filter({ has: page.getByRole("heading", { name }) });
}

test.describe("the home feed on a phone", () => {
  test("the blocks that grow vertically are capped", async ({ signedIn: page }) => {
    await openStubbedHome(page);

    const arrivals = section(page, /Fresh arrivals/i);
    const chart = section(page, /Your top this week/i);

    // Все двенадцать приехали в разметку — скрыты, а не выброшены, иначе тап по видимой
    // карточке ставил бы в очередь обрезанный контекст.
    await expect(arrivals.locator("a, button").filter({ hasText: /fresh track/i })).toHaveCount(
      SHELF_SIZE,
    );

    await expect(
      arrivals.locator("a, button").filter({ hasText: /fresh track/i, visible: true }),
    ).toHaveCount(4);

    await expect(chart.getByRole("listitem").filter({ visible: true })).toHaveCount(5);
  });

  test("shelves stop at eight cards", async ({ signedIn: page }) => {
    await openStubbedHome(page);

    const shelf = section(page, /^For you$/i);

    await expect(shelf.getByRole("listitem").filter({ visible: true })).toHaveCount(8);
  });

  test("the tail is hidden until it is asked for", async ({ signedIn: page }) => {
    await openStubbedHome(page);

    const showMore = page.getByRole("button", { name: "Show more" });
    await expect(showMore).toBeVisible();

    for (const name of [/New albums/i, /Artists for you/i, /Your playlists/i]) {
      await expect(section(page, name)).not.toBeVisible();
    }

    await showMore.click();

    for (const name of [/New albums/i, /Artists for you/i, /Your playlists/i]) {
      await expect(section(page, name)).toBeVisible();
    }

    await expect(showMore).toHaveCount(0);
  });

  test("the collapsed page stays under three screens of scroll", async ({ signedIn: page }) => {
    await openStubbedHome(page);

    const collapsed = await page.evaluate(() => document.scrollingElement?.scrollHeight ?? 0);

    // Не пиксель-в-пиксель, а сигнализация: до правок здесь было ~4380px.
    expect(collapsed).toBeLessThan(2600);

    await page.getByRole("button", { name: "Show more" }).click();

    expect(await page.evaluate(() => document.scrollingElement?.scrollHeight ?? 0)).toBeGreaterThan(
      collapsed,
    );
  });

  test("what the cap hides is still reachable through see all", async ({ signedIn: page }) => {
    await openStubbedHome(page);

    await section(page, /Your top this week/i)
      .getByRole("link", { name: /see all/i })
      .click();

    await expect(page).toHaveURL(/\/mixes\/top$/);
  });
});
