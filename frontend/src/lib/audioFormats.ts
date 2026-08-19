// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { AudioQuality, AudioQualityOption } from "@/lib/types";

export const ACCEPTED_EXTENSIONS = [".mp3", ".flac", ".m4a"] as const;

export const ACCEPT_ATTRIBUTE = ".mp3,.flac,.m4a,audio/mpeg,audio/flac,audio/mp4";

export function extensionOf(fileName: string): string {
  return /\.[a-z0-9]+$/i.exec(fileName)?.[0].toLowerCase() ?? "";
}

export function isAcceptedAudio(fileName: string): boolean {
  return (ACCEPTED_EXTENSIONS as readonly string[]).includes(extensionOf(fileName));
}

const FALLBACK_TIERS = ["High", "Normal", "Low"] as const;

export function bestFallbackTier(available: AudioQualityOption[]): AudioQuality | null {
  return FALLBACK_TIERS.find((tier) => available.some((option) => option.quality === tier)) ?? null;
}

export function playableTier(
  codec: string | null | undefined,
  wanted: AudioQuality,
  available: AudioQualityOption[],
): AudioQuality {
  if (wanted !== "Original" || canDecodeOriginal(codec)) return wanted;

  return bestFallbackTier(available) ?? wanted;
}

const MIME_FOR_CODEC: Record<string, string> = {
  mp3: "audio/mpeg",
  flac: "audio/flac",
  aac: 'audio/mp4; codecs="mp4a.40.2"',
  alac: 'audio/mp4; codecs="alac"',
};

const answers = new Map<string, boolean>();

export function canDecodeOriginal(codec: string | null | undefined): boolean {
  if (!codec) return true;

  const mime = MIME_FOR_CODEC[codec];
  if (!mime || typeof document === "undefined") return true;

  const remembered = answers.get(codec);
  if (remembered !== undefined) return remembered;

  const answer = document.createElement("audio").canPlayType(mime) !== "";
  answers.set(codec, answer);

  return answer;
}
