// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { api } from "./api";
import { sha256File } from "./fileHash";
import { readId3Tags } from "./id3";
import type { Track, UploadProbeFile, UploadProbeVerdict } from "./types";

export type FileVerdict = { verdict: UploadProbeVerdict; match: Track | null };

const HASH_CONCURRENCY = 2;

interface HashResponse {
  id: number;
  hash?: string;
}

let hashWorker: Worker | null = null;
let nextHashId = 0;
const pendingHashes = new Map<number, (hash?: string) => void>();

export function fileKey(file: File): string {
  return `${file.name}:${file.size}`;
}

export async function checkAgainstLibrary(files: File[]): Promise<Record<string, FileVerdict>> {
  if (files.length === 0) return {};

  const described = new Array<UploadProbeFile>(files.length);
  let next = 0;

  const worker = async () => {
    for (;;) {
      const index = next++;
      if (index >= files.length) return;
      described[index] = await describe(files[index]);
    }
  };

  await Promise.all(Array.from({ length: Math.min(HASH_CONCURRENCY, files.length) }, worker));

  const result = await api.checkUpload(described);

  const verdicts: Record<string, FileVerdict> = {};
  result.files.forEach((entry, index) => {
    const file = files[index];
    if (file) verdicts[fileKey(file)] = { verdict: entry.verdict, match: entry.match ?? null };
  });

  return verdicts;
}

async function describe(file: File): Promise<UploadProbeFile> {
  const tags = await readId3Tags(file);

  return {
    fileName: file.name,
    contentHash: await sha256Hex(file),
    title: tags.title,
    artist: tags.artist,
  };
}

async function sha256Hex(file: File): Promise<string | undefined> {
  try {
    if (typeof Worker === "undefined") return await sha256File(file);

    const worker = (hashWorker ??= createHashWorker());
    const id = nextHashId++;

    return await new Promise<string | undefined>((resolve) => {
      pendingHashes.set(id, resolve);
      worker.postMessage({ id, file });
    });
  } catch {
    return undefined;
  }
}

function createHashWorker(): Worker {
  const worker = new Worker(new URL("./fileHash.worker.ts", import.meta.url), { type: "module" });

  worker.addEventListener("message", (event: MessageEvent<HashResponse>) => {
    const { id, hash } = event.data;
    const resolve = pendingHashes.get(id);
    if (!resolve) return;

    pendingHashes.delete(id);
    resolve(hash);
  });

  worker.addEventListener("error", () => {
    for (const resolve of pendingHashes.values()) resolve(undefined);
    pendingHashes.clear();
    hashWorker = null;
    worker.terminate();
  });

  return worker;
}
