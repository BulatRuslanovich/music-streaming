// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { ActivityPoint } from "@/components/ActivityTable";
import type {
  AdminListenerSort,
  AdminUpload,
  AdminUploadSort,
  DailyUpload,
  IngestionSource,
  SortDirection,
  StatisticsPeriod,
} from "@/lib/types";

const PERIODS: StatisticsPeriod[] = ["Week", "Month", "Quarter", "Year", "All"];

const LISTENER_SORTS: AdminListenerSort[] = [
  "Username",
  "CreatedAt",
  "LastActiveAt",
  "ListenedSeconds",
  "Plays",
  "UploadedTracks",
  "UploadedBytes",
  "SkipRate",
];

const UPLOAD_SORTS: AdminUploadSort[] = ["CreatedAt", "FileSize", "Plays"];

const SOURCES: IngestionSource[] = ["Unknown", "WebUpload", "DirectoryImport"];

const DIRECTIONS: SortDirection[] = ["Asc", "Desc"];

/**
 * Разбор значения из query string.
 *
 * Адрес правит кто угодно, а значения едут в API как enum — не сходится, значит подставляем
 * умолчание, а не отправляем мусор на сервер и не падаем на рендере.
 */
function parser<T extends string>(allowed: T[]) {
  return (value: string | null | undefined, fallback: T): T =>
    allowed.includes(value as T) ? (value as T) : fallback;
}

export const parsePeriod = parser(PERIODS);
export const parseListenerSort = parser(LISTENER_SORTS);
export const parseUploadSort = parser(UPLOAD_SORTS);
export const parseDirection = parser(DIRECTIONS);

/** Источник как фильтр: пустая строка означает «все», а не «неизвестный». */
export function parseSource(value: string | null | undefined): IngestionSource | undefined {
  return SOURCES.includes(value as IngestionSource) ? (value as IngestionSource) : undefined;
}

/**
 * Кто числится добавившим трек.
 *
 * Три случая различаются намеренно: у веб-загрузки есть человек, у импорта из директории его
 * не бывает по устройству, а `Unknown` — это треки, добавленные до того, как источник начали
 * записывать. Свести последние два в один прочерк значило бы соврать про импорт.
 */
export type UploaderLabel =
  { kind: "user"; username: string } | { kind: "system" } | { kind: "unknown" };

export function uploaderLabel(upload: Pick<AdminUpload, "addedByUsername" | "ingestionSource">) {
  if (upload.addedByUsername) return { kind: "user", username: upload.addedByUsername } as const;
  if (upload.ingestionSource === "DirectoryImport") return { kind: "system" } as const;

  return { kind: "unknown" } as const;
}

/** Точки для графика загрузок: значение — число треков, а не секунды. */
export function uploadPoints(
  days: DailyUpload[],
  shortDate: (iso: string) => string,
): ActivityPoint[] {
  const every = Math.max(1, Math.ceil(days.length / 5));

  return days.map((day, index) => ({
    key: day.date,
    label: shortDate(day.date),
    value: day.tracks,
    plays: day.tracks,
    tick: index % every === 0 ? shortDate(day.date) : undefined,
  }));
}

/** Доля от 0 до 1 в проценты для подписи. Без событий сервер присылает 0. */
export function percent(share: number): number {
  return Math.round(share * 100);
}
