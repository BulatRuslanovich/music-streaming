// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export type TrackSort = "Title" | "Recent" | "Artist" | "Album";

export interface PageParams {
  page?: number;
  pageSize?: number;
}

export interface UploadProgress {
  percent: number;
  fileIndex: number;
  fileCount: number;
  fileName: string;
}
