// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { expect, owner, seededTrack, test } from "./fixtures";

test.describe("what an admin can see about the service", () => {
  test("the overview opens with its headline numbers and its charts", async ({
    signedIn: page,
  }) => {
    await page.goto("/admin/statistics");

    await expect(page.getByRole("heading", { name: "Service statistics" })).toBeVisible();

    await expect(page.getByText("Accounts", { exact: true })).toBeVisible();
    await expect(page.getByText("Size on disk")).toBeVisible();
    await expect(page.getByText("Skip rate").first()).toBeVisible();

    // Каждый график обязан иметь текстовую таблицу рядом — на ней держится доступность.
    await expect(page.getByRole("table", { name: /Activity/i }).first()).toBeAttached();

    await expect(page.getByText("State of the catalogue")).toBeVisible();
    await expect(page.getByText("Never played")).toBeVisible();
  });

  test("the period lives in the address, so the choice survives a reload", async ({
    signedIn: page,
  }) => {
    await page.goto("/admin/statistics");

    await page.getByRole("button", { name: "7 days" }).click();
    await expect(page).toHaveURL(/period=Week/);

    await page.reload();
    await expect(page.getByRole("button", { name: "7 days" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
  });

  test("the listener table leads to the page of one listener", async ({ signedIn: page }) => {
    await page.goto("/admin/statistics/users");

    await expect(page.getByRole("heading", { name: "Listeners" })).toBeVisible();

    const row = page.getByRole("row").filter({ hasText: "admin" }).first();
    await expect(row).toBeVisible();
    await row.getByRole("link").first().click();

    await expect(page).toHaveURL(/\/admin\/statistics\/users\/[0-9a-f-]+/);
    await expect(page.getByRole("link", { name: "All listeners" })).toBeVisible();
    await expect(page.getByText("Listening time").first()).toBeVisible();
  });

  test("the track the suite uploads is signed with the account that sent it", async ({
    signedIn: page,
  }) => {
    await page.goto("/admin/statistics/uploads");

    await expect(page.getByRole("heading", { name: "Uploads" })).toBeVisible();

    // Глобальная настройка засевает трек обычной HTTP-загрузкой от владельца — значит в
    // таблице он обязан быть подписан этим аккаунтом и помечен как загрузка через сайт.
    const row = page.getByRole("row").filter({ hasText: seededTrack.title }).first();

    await expect(row).toBeVisible();
    await expect(row.getByRole("link", { name: owner.username })).toBeVisible();
    await expect(row.getByText("Web upload")).toBeVisible();
  });

  test("filtering the uploads by source narrows the table", async ({ signedIn: page }) => {
    await page.goto("/admin/statistics/uploads?source=DirectoryImport");

    await expect(page.getByRole("heading", { name: "Uploads" })).toBeVisible();

    // Либо строки есть и все они системного импорта, либо честно показано пустое состояние.
    const empty = page.getByText("Nothing has been added yet.");
    const rows = page.getByRole("row");

    if (await empty.isVisible()) return;

    for (const row of await rows.all()) {
      const text = await row.textContent();
      if (text?.includes("Added by")) continue;
      expect(text).toContain("System import");
    }
  });

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
      data: { username, password, displayName: username, isAdmin: false },
    });
    expect(created.ok()).toBeTruthy();

    await page.getByRole("button", { name: "Sign out" }).first().click();
    await expect(page).toHaveURL(/\/login/);

    await page.locator("#username").fill(username);
    await page.locator("#password").fill(password);
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page).toHaveURL(/\/$/);

    // Настоящая защита — на бэкенде: раздел закрыт политикой Admin, а страница просто уводит.
    const denied = await page.request.get("/api/admin/statistics/overview");
    expect(denied.status()).toBe(403);

    await page.goto("/admin/statistics");
    await expect(page).toHaveURL(/\/$/);
  });
});
