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

export interface Artist {
  id: string;
  name: string;
  albumCount: number;
  trackCount: number;
  hasImage: boolean;
}

export type ArtistDetail = Omit<Artist, "albumCount" | "trackCount"> & {
  albums: Album[];
  tracks: Paged<Track>;
};

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

export type AlbumDetail = Omit<Album, "trackCount" | "createdAt"> & { tracks: Track[] };

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

export type PlaylistDetail = Omit<Playlist, "trackCount"> & { tracks: Track[] };

export interface SearchTopResult {
  kind: "Artist" | "Album" | "Track" | "Genre";
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
  freeBytes: number;
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

export type PageParams = { page?: number; pageSize?: number };

export type TrackSort = "Title" | "Recent" | "Artist" | "Album";

export interface LyricLine {
  at: number;
  text: string;
}

export interface Lyrics {
  trackId: string;
  plain: string;
  lines: LyricLine[];
  source: "Embedded" | "Manual" | "Provider";
}

export interface HomeBlock {
  key: string;
  baseKey: string;
  layout: "Shelf" | "Hero" | "Tile" | "QuickTiles" | "Grid" | "Chart" | "Circles";
  zone: "Lead" | "Quick" | "Browse";
  reason?: RecommendationReason | null;
  tracks?: Track[] | null;
  albums?: Album[] | null;
  artists?: Artist[] | null;
  playlists?: Playlist[] | null;
  totalCount?: number | null;
}

export interface HomeFeed {
  blocks: HomeBlock[];
  stats: LibraryStats;
}

export type HomeMixSlug = "daily" | "new" | "top";

export interface HomeMix {
  tracks: Track[];
}

export interface RecommendationReason {
  kind: string;
  subject?: string | null;
  subjectId?: string | null;
}

export interface QueueSignals {
  explore: boolean;
}

export interface RecommendedTrack {
  track: Track;
  reason: RecommendationReason;
  signals?: QueueSignals | null;
}

export interface RadioBatch {
  tracks: RecommendedTrack[];
  seedTrackId?: string | null;
}

export interface User {
  id: string;
  username: string;
  isAdmin: boolean;
}

export interface AdminUser extends User {
  isActive: boolean;
  createdAt: string;
}

export interface ClientConfig {
  maxUploadBytes: number;
  maxImageUploadBytes: number;
  accessTokenMinutes: number;
}

export type AudioQuality = "Low" | "Normal" | "Original";

export interface UserSettings {
  quality: AudioQuality;
  timeZone: string;
}

export interface UploadResult {
  uploaded: Track[];
  failed: { fileName: string; reason: string }[];
}

export interface UploadProgress {
  percent: number;
  fileIndex: number;
  fileCount: number;
  fileName: string;
}

export interface BulkDeleteResult {
  deleted: number;
  missing: string[];
}

export interface UploadProbeFile {
  fileName: string;
  contentHash?: string;
  title?: string;
  artist?: string;
}

export type UploadProbeVerdict = "New" | "Duplicate" | "Similar";

export type UploadProbeBasis = "None" | "Tags" | "Hash" | "HashAndTags";

export interface UploadProbeResult {
  files: {
    fileName: string;
    verdict: UploadProbeVerdict;
    basis: UploadProbeBasis;
    match?: Track;
  }[];
}
