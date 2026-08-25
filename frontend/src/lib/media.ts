// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { API_BASE } from "@/lib/http";
import type { AudioQuality, Track } from "@/lib/types";

export type CoverVariant = "thumb" | "full";

function sizeQuery(variant: CoverVariant): string {
  return variant === "thumb" ? "?size=thumb" : "";
}

export const mediaUrl = {
  stream: (trackId: string, quality: AudioQuality) =>
    `${API_BASE}/tracks/${trackId}/stream?quality=${quality}`,
  hls: (trackId: string, maxQuality: Exclude<AudioQuality, "Original">) =>
    `${API_BASE}/tracks/${trackId}/hls/master.m3u8?maxQuality=${maxQuality}`,
  trackCover: (trackId: string, variant: CoverVariant = "full") =>
    `${API_BASE}/tracks/${trackId}/cover${sizeQuery(variant)}`,
  albumCover: (albumId: string, variant: CoverVariant = "full") =>
    `${API_BASE}/albums/${albumId}/cover${sizeQuery(variant)}`,
  artistImage: (artistId: string) => `${API_BASE}/artists/${artistId}/image`,
  playlistCover: (playlistId: string) => `${API_BASE}/playlists/${playlistId}/cover`,
};

const imageVersions = new Map<string, number>();

function versioned(url: string, key: string): string {
  const version = imageVersions.get(key);
  return version === undefined ? url : `${url}${url.includes("?") ? "&" : "?"}v=${version}`;
}

function markImageChanged(key: string, changed: boolean) {
  if (changed) imageVersions.set(key, Date.now());
  else imageVersions.delete(key);
}

export function markArtistImageChanged(artistId: string, changed: boolean) {
  markImageChanged(`artist:${artistId}`, changed);
}

export function markPlaylistCoverChanged(playlistId: string, changed: boolean) {
  markImageChanged(`playlist:${playlistId}`, changed);
}

export function markAlbumCoverChanged(albumId: string, changed: boolean) {
  markImageChanged(`album:${albumId}`, changed);
}

export function artistImageUrl({
  artistId,
  hasImage = true,
}: {
  artistId?: string | null;
  hasImage?: boolean;
}): string | null {
  if (!hasImage || !artistId) return null;

  return versioned(mediaUrl.artistImage(artistId), `artist:${artistId}`);
}

export function playlistCoverUrl({
  playlistId,
  hasCover = false,
  coverTrackId,
  variant = "full",
}: {
  playlistId?: string | null;
  hasCover?: boolean;
  coverTrackId?: string | null;
  variant?: CoverVariant;
}): string | null {
  if (hasCover && playlistId) {
    return versioned(mediaUrl.playlistCover(playlistId), `playlist:${playlistId}`);
  }

  if (coverTrackId) return mediaUrl.trackCover(coverTrackId, variant);

  return null;
}

export function coverUrl({
  albumId,
  trackId,
  hasCover = true,
  variant = "full",
}: {
  albumId?: string | null;
  trackId?: string | null;
  hasCover?: boolean;
  variant?: CoverVariant;
}): string | null {
  if (!hasCover) return null;
  if (albumId) return versioned(mediaUrl.albumCover(albumId, variant), `album:${albumId}`);
  if (trackId) return mediaUrl.trackCover(trackId, variant);
  return null;
}

export function trackCoverUrl(
  track: Track | null | undefined,
  variant: CoverVariant = "full",
): string | null {
  if (!track) return null;
  return coverUrl({ albumId: track.albumId, trackId: track.id, hasCover: track.hasCover, variant });
}
