// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/**
 * Сколько элементов запрашивает у ленты главная. Живёт здесь, потому что тот же литерал зашит
 * в предзагрузку из <head> (earlyFetch.ts): takePreloaded сверяет URL строкой, и расхождение
 * не падает, а тихо промахивается мимо предзагруженного ответа и шлёт второй запрос.
 */
export const HOME_SECTION_SIZE = 12;

export type TrackSort = "Title" | "Recent" | "Artist" | "Album";

export interface PageParams {
  page?: number;
  pageSize?: number;
}

export interface UploadProgress {
  percent: number;
  fileIndex: number;
  fileCount: number;
  fileName: string;
}
