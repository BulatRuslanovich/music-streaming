// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { fileForm, query, request, requestFile } from "@/lib/http";
import { markArtistImageChanged } from "@/lib/media";
import type {
  Album,
  AlbumDetail,
  Artist,
  ArtistDetail,
  BulkDeleteResult,
  Genre,
  HomeFeed,
  HomeMix,
  HomeMixSlug,
  Paged,
  SearchResults,
  Track,
} from "@/lib/types";
import type { PageParams, TrackSort } from "./contracts";

export const catalogApi = {
  homeFeed: (sectionSize = 12) => request<HomeFeed>(`/home/feed${query({ sectionSize })}`),
  homeMix: (kind: HomeMixSlug) => request<HomeMix>(`/home/mixes/${kind}`),
  tracks: (params: PageParams & { sort?: TrackSort; q?: string } = {}) =>
    request<Paged<Track>>(`/tracks${query({ ...params })}`),
  shuffleTracks: (params: { limit?: number; q?: string } = {}) =>
    request<Track[]>(`/tracks/shuffle${query({ ...params })}`),
  artists: (params: PageParams & { q?: string } = {}) =>
    request<Paged<Artist>>(`/artists${query({ ...params })}`),
  artist: (id: string, params: PageParams = {}) =>
    request<ArtistDetail>(`/artists/${id}${query({ ...params })}`),
  albums: (params: PageParams & { artistId?: string; recentFirst?: boolean; q?: string } = {}) =>
    request<Paged<Album>>(`/albums${query({ ...params })}`),
  album: (id: string) => request<AlbumDetail>(`/albums/${id}`),
  genres: () => request<Genre[]>("/genres"),
  genreTracks: (id: string, params: PageParams = {}) =>
    request<Paged<Track>>(`/genres/${id}/tracks${query({ ...params })}`),
  search: (q: string, limit = 20) => request<SearchResults>(`/search${query({ q, limit })}`),

  updateTrack: (
    id: string,
    changes: {
      title?: string;
      artist?: string;
      album?: string;
      genre?: string;
      year?: number | null;
      trackNumber?: number | null;
      discNumber?: number | null;
    },
  ) => request<Track>(`/tracks/${id}`, { method: "PUT", body: changes }),
  deleteTrack: (id: string) => request<void>(`/tracks/${id}`, { method: "DELETE" }),
  deleteTracks: (ids: string[]) =>
    request<BulkDeleteResult>("/tracks/bulk-delete", { method: "POST", body: { ids } }),
  downloadTrack: (id: string, fallbackName: string) =>
    requestFile(`/tracks/${id}/download`, fallbackName),

  updateArtist: (id: string, name: string) =>
    request<Artist>(`/artists/${id}`, { method: "PUT", body: { name } }),
  uploadArtistImage: async (id: string, file: File) => {
    const artist = await request<Artist>(`/artists/${id}/image`, {
      method: "POST",
      body: fileForm(file),
    });
    markArtistImageChanged(id, true);
    return artist;
  },
  removeArtistImage: async (id: string) => {
    await request<void>(`/artists/${id}/image`, { method: "DELETE" });
    markArtistImageChanged(id, false);
  },
};
