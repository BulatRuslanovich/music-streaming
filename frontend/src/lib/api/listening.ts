// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { query, request } from "@/lib/http";
import type { MonthlyRecap } from "@/lib/recap";
import type {
  DjBatch,
  DjMode,
  DjVariety,
  HistoryEntry,
  Lyrics,
  Paged,
  RadioBatch,
  RecommendationSuppression,
  Statistics,
  StatisticsPeriod,
  SuppressionTarget,
  Track,
  UserSettings,
} from "@/lib/types";
import type { PageParams } from "./contracts";

export const listeningApi = {
  normalization: (id: string, mode: string, signal?: AbortSignal) =>
    request<{ gain: number; available: boolean }>(`/tracks/${id}/normalization${query({ mode })}`, {
      signal,
    }),
  monthlyRecap: (month?: string) => request<MonthlyRecap>(`/me/recap${query({ month })}`),
  saveRecapPlaylist: (month: string, name: string) =>
    request<{ id: string }>("/me/recap/playlist", { method: "POST", body: { month, name } }),
  suppressRecommendation: (target: SuppressionTarget, targetId: string) =>
    request<RecommendationSuppression>("/recommendations/feedback", {
      method: "POST",
      body: { target, targetId },
    }),
  restoreRecommendation: (target: SuppressionTarget, targetId: string) =>
    request<void>(`/recommendations/feedback/${target}/${targetId}`, { method: "DELETE" }),
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
