// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Track } from "./catalog";

export type StatisticsPeriod = "Week" | "Month" | "Quarter" | "Year" | "All";

export interface StatisticsEntry {
  id: string;
  name: string;
  listenedSeconds: number;
  plays: number;
  hasImage: boolean;
}

export interface StatisticsTrack {
  track: Track;
  listenedSeconds: number;
  plays: number;
}

export interface DailyActivity {
  date: string;
  listenedSeconds: number;
  plays: number;
}

export interface HourlyActivity {
  hour: number;
  listenedSeconds: number;
  plays: number;
}

interface StatisticsSummary {
  listenedSeconds: number;
  plays: number;
  uniqueTracks: number;
  uniqueArtists: number;
  uniqueAlbums: number;
  activeDays: number;
  peakDay?: DailyActivity | null;
}

export interface Statistics {
  period: StatisticsPeriod;
  from?: string | null;
  timeZone: string;
  summary: StatisticsSummary;
  topTracks: StatisticsTrack[];
  topArtists: StatisticsEntry[];
  topAlbums: StatisticsEntry[];
  topGenres: StatisticsEntry[];
  byDay: DailyActivity[];
  byHour: HourlyActivity[];
}
