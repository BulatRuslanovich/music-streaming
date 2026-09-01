// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";
import { queries } from "@/lib/queries";
import type { Playlist } from "@/lib/types";

/**
 * Список плейлистов для меню трека, загружаемый один раз на весь список строк: меню
 * открывают у одной строки, но живут они рядом, и без единой загрузки каждое открытие
 * стоило бы отдельного запроса.
 *
 * Раньше это был собственный кэш на `useRef`. Он дублировал то, что TanStack уже делает,
 * и вдобавок не знал об инвалидации: создали плейлист из меню трека — в меню соседней
 * строки его не было до размонтирования списка. `ensureQueryData` берёт данные из общего
 * кэша, схлопывает одновременные вызовы в один запрос и уважает `invalidate("playlists")`.
 */
export function usePlaylistsOnce(): () => Promise<Playlist[]> {
  const client = useQueryClient();

  return useCallback(() => client.ensureQueryData(queries.playlists()).catch(() => []), [client]);
}
