// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export interface ArtistRef {
  id: string;
  name: string;
}

export interface Track {
  id: string;
  title: string;
  artistId: string;
  artistName: string;
  artists?: ArtistRef[] | null;
  albumId?: string | null;
  albumTitle?: string | null;
  genreName?: string | null;
  trackNumber?: number | null;
  discNumber?: number | null;
  year?: number | null;
  durationSeconds: number;
  originalFileName: string;
  isFavorite: boolean;
  hasCover: boolean;
  hasLyrics: boolean;
  createdAt: string;
  codec?: string | null;
  bitrateKbps?: number | null;
  sampleRateHz?: number | null;
  bitsPerSample?: number | null;
}

/** Выходные данные записи. Приезжают отдельным запросом и только когда их попросили. */
export interface TrackAnalysis {
  tempoBpm?: number | null;
  tempoConfidence: number;
  key?: number | null;
  isMinor: boolean;
  keyStrength: number;
  loudnessDb: number;
  dynamicRangeDb: number;
  energy: number;
  brightness: number;
  analyzedAt: string;
}

export interface Artist {
  id: string;
  name: string;
  albumCount: number;
  trackCount: number;
  hasImage: boolean;
}

export interface ArtistDetail {
  id: string;
  name: string;
  hasImage: boolean;
  tags: TagWeight[];
  albums: Album[];
  tracks: Paged<Track>;
}

/**
 * Тег каталога. Сущности за ним нет — ключ и есть имя, поэтому в адресах он ездит строкой.
 * Хранится в нижнем регистре, показывается через `tagLabel`.
 */
export interface Tag {
  name: string;
  trackCount: number;
  coverAlbumIds: string[];
}

/** Тег конкретной записи: вес решает порядок и то, показывать ли его вообще. */
export interface TagWeight {
  name: string;
  weight: number;
}

export interface Album {
  id: string;
  title: string;
  artistId: string;
  artistName: string;
  year?: number | null;
  trackCount: number;
  durationSeconds: number;
  hasCover: boolean;
  createdAt: string;
}

export interface AlbumDetail {
  id: string;
  title: string;
  artistId: string;
  artistName: string;
  year?: number | null;
  hasCover: boolean;
  durationSeconds: number;
  tracks: Track[];
}

export interface Genre {
  id: string;
  name: string;
  trackCount: number;
  coverAlbumIds: string[];
}

export interface Playlist {
  id: string;
  name: string;
  description?: string | null;
  isPublic: boolean;
  ownerId: string;
  ownerName: string;
  trackCount: number;
  durationSeconds: number;
  hasCover: boolean;
  coverTrackId?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PlaylistDetail {
  id: string;
  name: string;
  description?: string | null;
  isPublic: boolean;
  ownerId: string;
  ownerName: string;
  durationSeconds: number;
  hasCover: boolean;
  coverTrackId?: string | null;
  createdAt: string;
  updatedAt: string;
  tracks: Track[];
}

type SearchResultKind = "Artist" | "Album" | "Track" | "Genre";

export interface SearchTopResult {
  kind: SearchResultKind;
  artist?: Artist | null;
  album?: Album | null;
  track?: Track | null;
  genre?: Genre | null;
}

export interface SearchResults {
  artists: Artist[];
  albums: Album[];
  tracks: Track[];
  genres: Genre[];
  top?: SearchTopResult | null;
}

export interface HistoryEntry {
  id: string;
  track: Track;
  playedAt: string;
  playbackPosition: number;
}

export interface LibraryStats {
  trackCount: number;
  albumCount: number;
  totalDurationSeconds: number;
  totalBytes: number;
  favoriteCount: number;
}

export interface LibraryOverview {
  stats: LibraryStats;
  recentTracks: Track[];
  recentAlbums: Album[];
  recentArtists: Artist[];
  topGenres: Genre[];
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
