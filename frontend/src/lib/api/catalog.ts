// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { fileForm, query, request, requestFile } from "@/lib/http";
import { markAlbumCoverChanged, markArtistImageChanged } from "@/lib/media";
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
  TrackAnalysis,
} from "@/lib/types";
import { HOME_SECTION_SIZE, type PageParams, type TrackSort } from "./contracts";

export const catalogApi = {
  homeFeed: (sectionSize: number = HOME_SECTION_SIZE, signal?: AbortSignal) =>
    request<HomeFeed>(`/home/feed${query({ sectionSize })}`, { signal }),
  homeMix: (kind: HomeMixSlug, signal?: AbortSignal) =>
    request<HomeMix>(`/home/mixes/${kind}`, { signal }),
  tracks: (params: PageParams & { sort?: TrackSort; q?: string } = {}, signal?: AbortSignal) =>
    request<Paged<Track>>(`/tracks${query({ ...params })}`, { signal }),
  shuffleTracks: (params: { limit?: number; q?: string } = {}) =>
    request<Track[]>(`/tracks/shuffle${query({ ...params })}`),
  trackAnalysis: (id: string) => request<TrackAnalysis>(`/tracks/${id}/analysis`),
  artists: (params: PageParams & { q?: string } = {}, signal?: AbortSignal) =>
    request<Paged<Artist>>(`/artists${query({ ...params })}`, { signal }),
  artist: (id: string, params: PageParams = {}, signal?: AbortSignal) =>
    request<ArtistDetail>(`/artists/${id}${query({ ...params })}`, { signal }),
  artistTopTracks: (id: string, limit = 10) =>
    request<Track[]>(`/artists/${id}/top-tracks${query({ limit })}`),
  albums: (
    params: PageParams & { artistId?: string; recentFirst?: boolean; q?: string } = {},
    signal?: AbortSignal,
  ) => request<Paged<Album>>(`/albums${query({ ...params })}`, { signal }),
  album: (id: string, signal?: AbortSignal) => request<AlbumDetail>(`/albums/${id}`, { signal }),
  genres: (signal?: AbortSignal) => request<Genre[]>("/genres", { signal }),
  genreTracks: (id: string, params: PageParams = {}, signal?: AbortSignal) =>
    request<Paged<Track>>(`/genres/${id}/tracks${query({ ...params })}`, { signal }),
  search: (q: string, limit = 20, signal?: AbortSignal) =>
    request<SearchResults>(`/search${query({ q, limit })}`, { signal }),
  searchTracks: (q: string, params: PageParams = {}, signal?: AbortSignal) =>
    request<Paged<Track>>(`/search/tracks${query({ q, ...params })}`, { signal }),
  searchAlbums: (q: string, params: PageParams = {}, signal?: AbortSignal) =>
    request<Paged<Album>>(`/search/albums${query({ q, ...params })}`, { signal }),
  searchArtists: (q: string, params: PageParams = {}, signal?: AbortSignal) =>
    request<Paged<Artist>>(`/search/artists${query({ q, ...params })}`, { signal }),
  searchGenres: (q: string, params: PageParams = {}, signal?: AbortSignal) =>
    request<Paged<Genre>>(`/search/genres${query({ q, ...params })}`, { signal }),

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
  updateAlbum: (id: string, changes: { title?: string; artist?: string; year?: number | null }) =>
    request<Album>(`/albums/${id}`, { method: "PUT", body: changes }),
  uploadAlbumCover: async (id: string, file: File) => {
    const album = await request<Album>(`/albums/${id}/cover`, {
      method: "POST",
      body: fileForm(file),
    });
    markAlbumCoverChanged(id, true);
    return album;
  },
  removeAlbumCover: async (id: string) => {
    await request<void>(`/albums/${id}/cover`, { method: "DELETE" });
    markAlbumCoverChanged(id, false);
  },
};
