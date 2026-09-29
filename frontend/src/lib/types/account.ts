// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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

export type AudioQuality = "Low" | "Normal" | "High" | "Original";

export interface UserSettings {
  quality: AudioQuality;
  dataSaver: boolean;
  timeZone: string;
}
