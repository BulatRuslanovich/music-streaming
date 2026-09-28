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

test.describe("who can reach the admin section", () => {
  test("an ordinary listener is bounced out of the admin section", async ({ page }) => {
    const username = `e2elistener${Date.now()}`.slice(0, 20);
    const password = "e2e-listener-password";

    // Слушателя заводит администратор — своей же ручкой из админки.
    await page.goto("/login");
    await page.locator("#username").fill("admin");
    await page.locator("#password").fill(process.env.E2E_PASSWORD ?? "smoke-test-owner-password");
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page).toHaveURL(/\/$/);

    const created = await page.request.post("/api/admin/users", {
      data: { username, password, isAdmin: false },
    });
    expect(created.ok()).toBeTruthy();

    await page.getByRole("button", { name: "Sign out" }).first().click();
    await expect(page).toHaveURL(/\/login/);

    await page.locator("#username").fill(username);
    await page.locator("#password").fill(password);
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page).toHaveURL(/\/$/);

    // Настоящая защита — на бэкенде: раздел закрыт политикой Admin, а страница просто уводит.
    const denied = await page.request.get("/api/admin/users");
    expect(denied.status()).toBe(403);

    await page.goto("/admin");
    await expect(page).toHaveURL(/\/$/);
  });
});
