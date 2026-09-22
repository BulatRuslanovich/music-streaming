// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export interface User {
  id: string;
  username: string;
  displayName: string;
  isAdmin: boolean;
}

export interface AdminUser extends User {
  isActive: boolean;
  createdAt: string;
}

export interface SystemInfo {
  version: string;
  commit?: string;
  builtAt?: string;
}

export interface ClientConfig {
  historyThresholdSeconds: number;
  maxUploadBytes: number;
  maxImageUploadBytes: number;
  hlsEnabled: boolean;
  audioQualities: AudioQualityOption[];
  accessTokenMinutes: number;
}

export type AudioQuality = "Low" | "Normal" | "High" | "Original";

export interface AudioQualityOption {
  quality: AudioQuality;
  bitrateKbps?: number | null;
}

export interface UserSettings {
  autoplay: boolean;
  quality: AudioQuality;
  dataSaver: boolean;
  timeZone: string;
}

export interface LastfmStatus {
  available: boolean;
  username?: string | null;
  lastScrobbleAt?: string | null;
}
