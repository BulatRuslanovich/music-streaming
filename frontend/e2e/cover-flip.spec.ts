// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { expect, seededTrackRow, test } from "./fixtures";

/**
 * Оборот обложки живёт под двойным щелчком, и поймать его регрессию можно только в браузере.
 * Ломался он уже дважды, и оба раза одинаково снаружи — щелчок выделял арт и больше ничего:
 * сначала обработчик висел не на том узле, потом всплытие гасил оверлей с кнопками, который
 * лежит `inset-0` поверх всей обложки и на ховере забирает указатель себе.
 */
test.describe("cover back side", () => {
  test("double-clicking the artwork turns it over and back", async ({ signedIn: page }) => {
    const row = await seededTrackRow(page);
    await row.dblclick();

    await page.getByRole("button", { name: "Open the full player" }).first().click();

    const cover = page.locator("[data-player-fullscreen] [data-side]");
    await expect(cover).toHaveAttribute("data-side", "front");

    await cover.dblclick();
    await expect(cover).toHaveAttribute("data-side", "back");

    // Формат берётся из самого трека, поэтому оборот не бывает пустым даже у записи,
    // которую анализатор ещё не трогал.
    await expect(page.getByText("Pressing details")).toBeVisible();
    await expect(page.getByText("Format")).toBeVisible();

    await cover.dblclick();
    await expect(cover).toHaveAttribute("data-side", "front");
  });
});
