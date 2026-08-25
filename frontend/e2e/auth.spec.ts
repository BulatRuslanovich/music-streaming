// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { expect, owner, signIn, test } from "./fixtures";

test.describe("signing in", () => {
  test("a signed-out visitor is sent to the sign-in page", async ({ page }) => {
    await page.goto("/");

    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
  });

  test("the wrong password is refused and the field is cleared", async ({ page }) => {
    await page.goto("/login");

    await page.locator("#username").fill(owner.username);
    await page.locator("#password").fill("definitely-not-the-password");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.getByText(/invalid username or password/i)).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.locator("#password")).toHaveValue("");
  });

  test("the owner signs in and lands on the home page", async ({ page }) => {
    await signIn(page);

    await expect(page.getByRole("navigation").first()).toBeVisible();
  });

  test("signing out sends the browser back to the sign-in page", async ({ signedIn: page }) => {
    await page.getByRole("button", { name: "Sign out" }).first().click();

    await expect(page).toHaveURL(/\/login$/);
  });
});
