// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { request, type FullConfig } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
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

    // Путь от корня фронтенда, а не от `import.meta.url`: Playwright транспилирует
    // конфигурацию в CJS (у пакета нет `"type": "module"`), и там `import.meta` невалиден —
    // сьют падал на разборе ещё до первого теста.
    const fixture = await readFile(join(process.cwd(), "e2e/fixtures/track.mp3"));

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
