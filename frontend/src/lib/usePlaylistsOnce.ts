// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useRef } from "react";
import { api } from "@/lib/api";
import type { Playlist } from "@/lib/types";

/**
 * Список плейлистов для меню трека, загружаемый один раз на весь список строк: меню
 * открывают у одной строки, но живут они рядом, и без single-flight каждое открытие
 * стоило бы отдельного запроса.
 */
export function usePlaylistsOnce(): () => Promise<Playlist[]> {
  const request = useRef<Promise<Playlist[]> | null>(null);

  return useCallback(() => {
    request.current ??= api.playlists().catch(() => []);
    return request.current;
  }, []);
}
