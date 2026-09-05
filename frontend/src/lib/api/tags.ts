// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { query, request } from "@/lib/http";
import type { Artist, Paged, Tag, TagWeight, Track } from "@/lib/types";
import type { PageParams } from "./contracts";

/**
 * Имя тега везде едет параметром запроса, а не куском пути: теги приходят из Last.fm как есть,
 * среди них попадается «rock/pop», и слэш в сегменте пути пришлось бы протаскивать закодированным
 * через маршрутизацию обеих сторон.
 */
export const tagsApi = {
  tags: (limit?: number) => request<Tag[]>(`/tags${query({ limit })}`),
  tagTracks: (name: string, params: PageParams = {}) =>
    request<Paged<Track>>(`/tags/tracks${query({ name, ...params })}`),
  tagArtists: (name: string, limit?: number) =>
    request<Artist[]>(`/tags/artists${query({ name, limit })}`),
  trackTags: (id: string) => request<TagWeight[]>(`/tracks/${id}/tags`),
};
