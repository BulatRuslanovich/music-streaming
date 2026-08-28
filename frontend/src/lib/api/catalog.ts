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
} from "@/lib/types";
import { HOME_SECTION_SIZE, type PageParams, type TrackSort } from "./contracts";

export const catalogApi = {
  homeFeed: (sectionSize: number = HOME_SECTION_SIZE) =>
    request<HomeFeed>(`/home/feed${query({ sectionSize })}`),
  homeMix: (kind: HomeMixSlug) => request<HomeMix>(`/home/mixes/${kind}`),
  tracks: (params: PageParams & { sort?: TrackSort; q?: string } = {}) =>
    request<Paged<Track>>(`/tracks${query({ ...params })}`),
  shuffleTracks: (params: { limit?: number; q?: string } = {}) =>
    request<Track[]>(`/tracks/shuffle${query({ ...params })}`),
  artists: (params: PageParams & { q?: string } = {}) =>
    request<Paged<Artist>>(`/artists${query({ ...params })}`),
  artist: (id: string, params: PageParams = {}) =>
    request<ArtistDetail>(`/artists/${id}${query({ ...params })}`),
  artistTopTracks: (id: string, limit = 10) =>
    request<Track[]>(`/artists/${id}/top-tracks${query({ limit })}`),
  albums: (params: PageParams & { artistId?: string; recentFirst?: boolean; q?: string } = {}) =>
    request<Paged<Album>>(`/albums${query({ ...params })}`),
  album: (id: string) => request<AlbumDetail>(`/albums/${id}`),
  genres: () => request<Genre[]>("/genres"),
  genreTracks: (id: string, params: PageParams = {}) =>
    request<Paged<Track>>(`/genres/${id}/tracks${query({ ...params })}`),
  search: (q: string, limit = 20) => request<SearchResults>(`/search${query({ q, limit })}`),
  searchTracks: (q: string, params: PageParams = {}) =>
    request<Paged<Track>>(`/search/tracks${query({ q, ...params })}`),
  searchAlbums: (q: string, params: PageParams = {}) =>
    request<Paged<Album>>(`/search/albums${query({ q, ...params })}`),
  searchArtists: (q: string, params: PageParams = {}) =>
    request<Paged<Artist>>(`/search/artists${query({ q, ...params })}`),
  searchGenres: (q: string, params: PageParams = {}) =>
    request<Paged<Genre>>(`/search/genres${query({ q, ...params })}`),

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
