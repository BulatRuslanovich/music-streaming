"use client";

import {ReactNode, useState} from "react";
import { artistImageUrl, coverUrl, playlistCoverUrl, type CoverVariant } from "@/lib/media";
import { accentFor, initialsFor } from "@/lib/format";
import { useT } from "@/contexts/I18nContext";
import { NoteIcon } from "./Icons";

interface CoverProps {
  albumId?: string | null;
  trackId?: string | null;
  artistId?: string | null;
  playlistId?: string | null;
  coverTrackId?: string | null;
  hasCover?: boolean;
  name: string;
  size?: number | string;
  variant?: CoverVariant;
  rounded?: boolean;
  className?: string;
  fallback?: ReactNode;
}

export function Cover({
  albumId,
  trackId,
  artistId,
  playlistId,
  coverTrackId,
  hasCover = true,
  name,
  size = "100%",
  variant = "thumb",
  rounded = false,
  className = "",
  fallback,
}: CoverProps) {
  const [failed, setFailed] = useState(false);
  const t = useT();

  const source = artistId
    ? artistImageUrl({ artistId, hasImage: hasCover })
    : playlistId
      ? playlistCoverUrl({ playlistId, hasCover, coverTrackId, variant })
      : coverUrl({ albumId, trackId, hasCover, variant });
  const showImage = source !== null && !failed;

  const style = {
    width: typeof size === "number" ? `${size}px` : size,
    height: typeof size === "number" ? `${size}px` : size,
    ...(showImage ? {} : { background: accentFor(name || "?") }),
  };

  return (
    <div
      className={`cover ${rounded ? "cover-round" : ""} ${className}`}
      style={style}
      data-placeholder={showImage ? undefined : "true"}
    >
      {showImage ? (
        <img src={source!} alt={t("cover.alt", { name })} loading="lazy" onError={() => setFailed(true)} />
      ) : (
        <span className="cover-fallback" aria-hidden="true">
          {fallback ?? (rounded ? initialsFor(name) : <NoteIcon size={22} />)}
        </span>
      )}
    </div>
  );
}
