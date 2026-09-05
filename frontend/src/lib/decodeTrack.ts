// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { audioOutput } from "./audioOutput";
import { fetchMedia } from "./http";

const MAX_DOWNLOAD_BYTES = 32 * 1024 * 1024;
const MAX_PCM_BYTES = 128 * 1024 * 1024;

export async function decodeTrack(url: string, signal: AbortSignal): Promise<AudioBuffer> {
  const response = await fetchMedia(url, { signal });
  if (
    !response.ok ||
    !response.body ||
    Number(response.headers.get("content-length")) > MAX_DOWNLOAD_BYTES
  ) {
    await response.body?.cancel();
    throw new Error("Track cannot be prepared within the audio buffer limit");
  }
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > MAX_DOWNLOAD_BYTES) throw new Error("Track exceeds audio buffer limit");
      chunks.push(value);
    }
  } finally {
    await reader.cancel().catch(() => {});
    reader.releaseLock();
  }
  if (signal.aborted) throw new DOMException("Aborted", "AbortError");
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  const buffer = await audioOutput.getContext().decodeAudioData(bytes.buffer);
  if (signal.aborted) throw new DOMException("Aborted", "AbortError");
  if (buffer.length * buffer.numberOfChannels * 4 > MAX_PCM_BYTES)
    throw new Error("Decoded track exceeds audio buffer limit");
  return buffer;
}
