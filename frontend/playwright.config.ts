// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "https://localhost:8443";

export default defineConfig({
  testDir: "./e2e",
  outputDir: "./e2e/.results",

  globalSetup: "./e2e/global-setup.ts",

  fullyParallel: false,
  workers: 1,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,

  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : [["list"]],

  timeout: 30_000,
  expect: { timeout: 10_000 },

  use: {
    baseURL,

    ignoreHTTPSErrors: true,

    locale: "en-US",

    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },

  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        launchOptions: {
          args: ["--autoplay-policy=no-user-gesture-required"],
        },
      },
    },
  ],
});
