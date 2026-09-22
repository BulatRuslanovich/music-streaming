// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Track } from "./catalog";

export interface UploadResult {
  uploaded: Track[];
  failed: { fileName: string; reason: string }[];
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

interface ImportFailure {
  fileName: string;
  reason: string;
}

export interface LibraryImportStatus {
  enabled: boolean;
  directory: string;
  running: boolean;
  waiting: number;
  pending: number;
  imported: number;
  failed: number;
  currentFile?: string | null;
  recentFailures: ImportFailure[];
}
