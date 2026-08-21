// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { query, request } from "@/lib/http";
import type {
  DjBatch,
  DjMode,
  DjVariety,
  HistoryEntry,
  Lyrics,
  Paged,
  RadioBatch,
  RecommendedTrack,
  Statistics,
  StatisticsPeriod,
  Track,
  UserSettings,
} from "@/lib/types";
import type { PageParams } from "./contracts";

export const listeningApi = {
  similarTracks: (trackId: string, limit = 20) =>
    request<RecommendedTrack[]>(`/recommendations/similar/${trackId}${query({ limit })}`),
  history: (params: PageParams = {}) =>
    request<Paged<HistoryEntry>>(`/history${query({ ...params })}`),
  recentlyPlayed: (params: PageParams = {}) =>
    request<Paged<Track>>(`/history/recent${query({ ...params })}`),
  recordPlay: (trackId: string, playbackPosition: number) =>
    request<void>("/history", { method: "POST", body: { trackId, playbackPosition } }),
  clearHistory: () => request<void>("/history", { method: "DELETE" }),
  settings: () => request<UserSettings>("/me/settings"),
  updateSettings: (changes: Partial<UserSettings>) =>
    request<UserSettings>("/me/settings", { method: "PUT", body: changes }),
  statistics: (period: StatisticsPeriod) =>
    request<Statistics>(`/me/statistics${query({ period })}`),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>("/me/password", { method: "POST", body: { currentPassword, newPassword } }),
  lyrics: (trackId: string) => request<Lyrics | null>(`/tracks/${trackId}/lyrics`),
  updateLyrics: (trackId: string, text: string) =>
    request<Lyrics | null>(`/tracks/${trackId}/lyrics`, { method: "PUT", body: { text } }),
  radio: (seedTrackId: string | null, exclude: string[], limit?: number) =>
    request<RadioBatch>("/recommendations/radio", {
      method: "POST",
      body: { seedTrackId, exclude, limit },
    }),
  dj: (
    mode: DjMode,
    variety: DjVariety,
    seedTrackId: string | null,
    exclude: string[],
    limit?: number,
  ) =>
    request<DjBatch>("/recommendations/dj", {
      method: "POST",
      body: { mode, variety, seedTrackId, exclude, limit },
    }),
};
