// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { fileForm, query, request } from "@/lib/http";
import { markPlaylistCoverChanged } from "@/lib/media";
import type { Paged, Playlist, PlaylistDetail, Track } from "@/lib/types";
import type { PageParams } from "./contracts";

export const libraryApi = {
  favorites: (params: PageParams = {}) =>
    request<Paged<Track>>(`/favorites${query({ ...params })}`),
  addFavorite: (trackId: string) =>
    request<void>(`/tracks/${trackId}/favorite`, { method: "POST" }),
  removeFavorite: (trackId: string) =>
    request<void>(`/tracks/${trackId}/favorite`, { method: "DELETE" }),
  playlists: () => request<Playlist[]>("/playlists"),
  publicPlaylists: () => request<Playlist[]>("/playlists/public"),
  playlist: (id: string) => request<PlaylistDetail>(`/playlists/${id}`),
  createPlaylist: (name: string, description?: string, isPublic = false) =>
    request<Playlist>("/playlists", { method: "POST", body: { name, description, isPublic } }),
  updatePlaylist: (id: string, name: string, description?: string | null, isPublic = false) =>
    request<Playlist>(`/playlists/${id}`, {
      method: "PUT",
      body: { name, description, isPublic },
    }),
  deletePlaylist: (id: string) => request<void>(`/playlists/${id}`, { method: "DELETE" }),
  addToPlaylist: (playlistId: string, trackId: string) =>
    request<void>(`/playlists/${playlistId}/tracks`, { method: "POST", body: { trackId } }),
  removeFromPlaylist: (playlistId: string, trackId: string) =>
    request<void>(`/playlists/${playlistId}/tracks/${trackId}`, { method: "DELETE" }),
  reorderPlaylist: (playlistId: string, trackIds: string[]) =>
    request<void>(`/playlists/${playlistId}/tracks/order`, { method: "PUT", body: { trackIds } }),
  uploadPlaylistCover: async (id: string, file: File) => {
    const playlist = await request<Playlist>(`/playlists/${id}/cover`, {
      method: "POST",
      body: fileForm(file),
    });
    markPlaylistCoverChanged(id, true);
    return playlist;
  },
  removePlaylistCover: async (id: string) => {
    await request<void>(`/playlists/${id}/cover`, { method: "DELETE" });
    markPlaylistCoverChanged(id, false);
  },
};
