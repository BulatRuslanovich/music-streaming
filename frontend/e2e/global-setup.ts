// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { request, type FullConfig } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { owner, seededTrack } from "./fixtures";

export default async function globalSetup(config: FullConfig) {
  const baseURL = config.projects[0]?.use.baseURL ?? "https://localhost:8443";

  const api = await request.newContext({ baseURL, ignoreHTTPSErrors: true });

  try {
    const signIn = await api.post("/api/auth/login", {
      data: { username: owner.username, password: owner.password },
    });

    if (!signIn.ok()) {
      throw new Error(
        `could not sign in as ${owner.username}: ${signIn.status()} ${await signIn.text()}`,
      );
    }

    const found = await api.get(
      `/api/tracks?q=${encodeURIComponent(seededTrack.title)}&pageSize=1`,
    );

    if (found.ok() && ((await found.json()).items ?? []).length > 0) return;

    const fixture = await readFile(fileURLToPath(new URL("fixtures/track.mp3", import.meta.url)));

    const upload = await api.post("/api/tracks/upload", {
      headers: {
        "Content-Type": "audio/mpeg",
        "X-File-Name": encodeURIComponent("caimack-e2e.mp3"),
      },
      data: fixture,
    });

    if (!upload.ok()) {
      throw new Error(`could not seed a track: ${upload.status()} ${await upload.text()}`);
    }
  } finally {
    await api.dispose();
  }
}
