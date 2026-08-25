// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { test as base, expect, type Page } from "@playwright/test";

export const owner = {
  username: process.env.E2E_USERNAME ?? "admin",
  password: process.env.E2E_PASSWORD ?? "smoke-test-owner-password",
};

/** Title of the track the global setup puts in the library. */
export const seededTrack = {
  title: "Caimack E2E Track",
  artist: "Caimack E2E Artist",
  album: "Caimack E2E Album",
};

/**
 * Locale comes from localStorage, and every label these tests match on is English.
 * Pinning it here keeps the selectors from depending on the runner's language.
 */
async function pinEnglishLocale(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem("music-streaming.locale", "en");
  });
}

export async function signIn(page: Page) {
  await page.goto("/login");

  await page.locator("#username").fill(owner.username);
  await page.locator("#password").fill(owner.password);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page).toHaveURL(/\/$/);
}

export const test = base.extend<{ signedIn: Page }>({
  page: async ({ page }, use) => {
    await pinEnglishLocale(page);
    await use(page);
  },

  signedIn: async ({ page }, use) => {
    await signIn(page);
    await use(page);
  },
});

export { expect };
