// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { tr } from "@/lib/i18n";
import { API_BASE, ApiError, GATEWAY_STATUSES, refreshSession, request } from "@/lib/http";
import type { UploadProbeFile, UploadProbeResult, UploadResult } from "@/lib/types";
import type { UploadProgress } from "./contracts";

const UPLOAD_CONCURRENCY = 3;

export const uploadApi = {
  upload: (
    files: File[],
    onProgress?: (progress: UploadProgress) => void,
    onFileDone?: (result: UploadResult) => void,
  ) => uploadWithProgress(files, onProgress, onFileDone),

  checkUpload: (files: UploadProbeFile[]) =>
    request<UploadProbeResult>("/tracks/upload/check", { method: "POST", body: { files } }),
};

async function uploadWithProgress(
  files: File[],
  onProgress?: (progress: UploadProgress) => void,
  onFileDone?: (result: UploadResult) => void,
): Promise<UploadResult> {
  const totalBytes = files.reduce((sum, file) => sum + file.size, 0);
  const results = new Array<UploadResult | null>(files.length).fill(null);
  const loaded = new Array<number>(files.length).fill(0);

  let completed = 0;
  let fatal: unknown = null;
  let lastReported = "";

  const report = () => {
    if (!onProgress) return;

    const sent = loaded.reduce((sum, bytes) => sum + bytes, 0);
    const percent = totalBytes === 0 ? 100 : Math.round((sent / totalBytes) * 100);
    const at = Math.min(completed, Math.max(files.length - 1, 0));
    const key = `${percent}:${at}`;
    if (key === lastReported) return;
    lastReported = key;

    onProgress({
      percent,
      fileIndex: at,
      fileCount: files.length,
      fileName: files[at]?.name ?? "",
    });
  };

  let next = 0;

  const worker = async () => {
    for (;;) {
      if (fatal !== null) return;

      const index = next++;
      if (index >= files.length) return;

      const file = files[index];
      let outcome: UploadResult;

      try {
        outcome = await uploadOneFileSigned(file, (bytes) => {
          loaded[index] = bytes;
          report();
        });
      } catch (reason) {
        if (reason instanceof ApiError && reason.status === 401) {
          fatal ??= reason;
          return;
        }

        outcome = {
          uploaded: [],
          failed: [
            {
              fileName: file.name,
              reason: reason instanceof Error ? reason.message : tr("upload.noConnection"),
            },
          ],
        };
      }

      results[index] = outcome;
      loaded[index] = file.size;
      completed += 1;
      report();
      onFileDone?.(outcome);
    }
  };

  report();
  await Promise.all(Array.from({ length: Math.min(UPLOAD_CONCURRENCY, files.length) }, worker));

  if (fatal !== null) throw fatal;

  const uploaded: UploadResult["uploaded"] = [];
  const failed: UploadResult["failed"] = [];

  for (const result of results) {
    if (!result) continue;
    uploaded.push(...result.uploaded);
    failed.push(...result.failed);
  }

  return { uploaded, failed };
}

async function uploadOneFileSigned(
  file: File,
  onLoaded: (bytes: number) => void,
): Promise<UploadResult> {
  try {
    return await uploadOneFile(file, onLoaded);
  } catch (reason) {
    if (!(reason instanceof ApiError) || reason.status !== 401) throw reason;
    if (!(await refreshSession())) throw reason;

    return uploadOneFile(file, onLoaded);
  }
}

function uploadOneFile(file: File, onLoaded: (bytes: number) => void): Promise<UploadResult> {
  return new Promise((resolve, reject) => {
    const form = new FormData();
    form.append("files", file);

    const xhr = new XMLHttpRequest();
    xhr.open("POST", `${API_BASE}/tracks/upload`);
    xhr.withCredentials = true;

    xhr.upload.addEventListener("progress", (event) => {
      if (event.lengthComputable) onLoaded(event.loaded);
    });

    xhr.addEventListener("load", () => {
      let parsed: unknown = null;
      try {
        parsed = JSON.parse(xhr.responseText);
      } catch {}

      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(parsed as UploadResult);
        return;
      }

      if (xhr.status === 400 && parsed && typeof parsed === "object" && "failed" in parsed) {
        resolve(parsed as UploadResult);
        return;
      }

      if (GATEWAY_STATUSES.has(xhr.status)) {
        reject(new ApiError(xhr.status, tr("error.unreachable")));
        return;
      }

      const problem = parsed as { detail?: string; title?: string } | null;
      reject(
        new ApiError(
          xhr.status,
          problem?.detail ?? problem?.title ?? tr("upload.failedStatus", { status: xhr.status }),
        ),
      );
    });

    xhr.addEventListener("error", () => reject(new ApiError(0, tr("upload.noConnection"))));
    xhr.addEventListener("abort", () => reject(new ApiError(0, tr("upload.cancelled"))));
    xhr.send(form);
  });
}
