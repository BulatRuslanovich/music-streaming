import { api } from "./api";
import { readId3Tags } from "./id3";
import type { Track, UploadProbeFile, UploadProbeVerdict } from "./types";

export type FileVerdict = { verdict: UploadProbeVerdict; match: Track | null };

export function fileKey(file: File): string {
  return `${file.name}:${file.size}`;
}

export async function checkAgainstLibrary(files: File[]): Promise<Record<string, FileVerdict>> {
  if (files.length === 0) return {};

  const described: UploadProbeFile[] = [];
  for (const file of files) {
    described.push(await describe(file));
  }

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
  if (!globalThis.crypto?.subtle) return undefined;

  try {
    const digest = await crypto.subtle.digest("SHA-256", await file.arrayBuffer());
    return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join(
      "",
    );
  } catch {
    return undefined;
  }
}
