// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

/** Каким путём трек попал в библиотеку. `Unknown` — добавлен до того, как это начали записывать. */
import type {
  DailyActivity,
  HourlyActivity,
  StatisticsEntry,
  StatisticsPeriod,
  StatisticsTrack,
} from "./statistics";

export type IngestionSource = "Unknown" | "WebUpload" | "DirectoryImport";

export type AdminListenerSort =
  | "Username"
  | "CreatedAt"
  | "LastActiveAt"
  | "ListenedSeconds"
  | "Plays"
  | "UploadedTracks"
  | "UploadedBytes"
  | "SkipRate";

export type AdminUploadSort = "CreatedAt" | "FileSize" | "Plays";

export type SortDirection = "Asc" | "Desc";

export interface IngestionSourceCount {
  source: IngestionSource;
  tracks: number;
}

export interface DailyUpload {
  date: string;
  tracks: number;
  bytes: number;
}

export interface AdminOverview {
  period: StatisticsPeriod;
  from?: string | null;
  timeZone: string;
  users: {
    total: number;
    /** Слушал хоть что-нибудь за выбранный период. */
    active: number;
    new: number;
  };
  library: {
    totalTracks: number;
    tracksAddedInPeriod: number;
    totalBytes: number;
    totalDurationSeconds: number;
  };
  listening: {
    listenedSeconds: number;
    plays: number;
    uniqueListeners: number;
    uniqueTracks: number;
    completed: number;
    skipped: number;
    skipRate: number;
  };
  activityByDay: DailyActivity[];
  uploadsByDay: DailyUpload[];
  uploadsBySource: IngestionSourceCount[];
}

export interface AdminListener {
  id: string;
  username: string;
  displayName: string;
  isAdmin: boolean;
  isActive: boolean;
  createdAt: string;
  /** За всё время, а не внутри выбранного периода. */
  lastActiveAt?: string | null;
  listenedSeconds: number;
  plays: number;
  uniqueTracks: number;
  uploadedTracks: number;
  uploadedBytes: number;
  likes: number;
  playlists: number;
  skipRate: number;
}

export interface PlaybackSourceCount {
  source: string;
  plays: number;
}

export interface AdminUploadedTrack {
  id: string;
  title: string;
  artistName: string;
  createdAt: string;
  fileSize: number;
}

export interface AdminListenerDetail {
  period: StatisticsPeriod;
  from?: string | null;
  timeZone: string;
  listener: AdminListener;
  topTracks: StatisticsTrack[];
  topArtists: StatisticsEntry[];
  topAlbums: StatisticsEntry[];
  topGenres: StatisticsEntry[];
  byDay: DailyActivity[];
  byHour: HourlyActivity[];
  bySource: PlaybackSourceCount[];
  recentUploads: AdminUploadedTrack[];
}

export interface AdminUpload {
  trackId: string;
  title: string;
  artistName: string;
  createdAt: string;
  /** null у импорта из директории и у треков старше этого поля. */
  addedByUserId?: string | null;
  addedByUsername?: string | null;
  ingestionSource: IngestionSource;
  originalFileName: string;
  fileSize: number;
  durationSeconds: number;
  codec?: string | null;
  bitrateKbps?: number | null;
  plays: number;
  uniqueListeners: number;
}

export interface AdminCatalogHealth {
  totalTracks: number;
  withoutCover: number;
  withoutLyrics: number;
  withoutGenre: number;
  withoutAlbum: number;
  withoutYear: number;
  neverListened: number;
  highSkipRate: number;
  highSkipRateThreshold: number;
  highSkipRateMinimumEvents: number;
}
