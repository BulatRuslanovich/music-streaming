import { API_BASE } from "@/lib/http";
import type { Track } from "@/lib/types";

export type CoverVariant = "thumb" | "full";

function sizeQuery(variant: CoverVariant): string {
  return variant === "thumb" ? "?size=thumb" : "";
}

export type AudioQuality = "original" | "low";

export const mediaUrl = {
  stream: (trackId: string, quality: AudioQuality = "original") =>
    `${API_BASE}/tracks/${trackId}/stream${quality === "low" ? "?quality=low" : ""}`,
  trackCover: (trackId: string, variant: CoverVariant = "full") =>
    `${API_BASE}/tracks/${trackId}/cover${sizeQuery(variant)}`,
  albumCover: (albumId: string, variant: CoverVariant = "full") =>
    `${API_BASE}/albums/${albumId}/cover${sizeQuery(variant)}`,
  artistImage: (artistId: string) => `${API_BASE}/artists/${artistId}/image`,
};

const artistImageVersions = new Map<string, number>();

export function markArtistImageChanged(artistId: string, changed: boolean) {
  if (changed) artistImageVersions.set(artistId, Date.now());
  else artistImageVersions.delete(artistId);
}

export function artistImageUrl({
  artistId,
  hasImage = true,
}: {
  artistId?: string | null;
  hasImage?: boolean;
}): string | null {
  if (!hasImage || !artistId) return null;

  const version = artistImageVersions.get(artistId);
  return version === undefined
    ? mediaUrl.artistImage(artistId)
    : `${mediaUrl.artistImage(artistId)}?v=${version}`;
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
  if (albumId) return mediaUrl.albumCover(albumId, variant);
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
