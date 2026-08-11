"use client";

import { useState } from "react";
import { artistImageUrl, coverUrl, type CoverVariant } from "@/lib/api";
import { accentFor, initialsFor } from "@/lib/format";
import { useT } from "@/contexts/I18nContext";
import { NoteIcon } from "./Icons";

interface CoverProps {
  albumId?: string | null;
  trackId?: string | null;
  artistId?: string | null;
  hasCover?: boolean;
  name: string;
  size?: number | string;
  variant?: CoverVariant;
  rounded?: boolean;
  className?: string;
}

export function Cover({
  albumId,
  trackId,
  artistId,
  hasCover = true,
  name,
  size = "100%",
  variant = "thumb",
  rounded = false,
  className = "",
}: CoverProps) {
  const [failed, setFailed] = useState(false);
  const t = useT();

  const source = artistId
    ? artistImageUrl({ artistId, hasImage: hasCover })
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
          {rounded ? initialsFor(name) : <NoteIcon size={22} />}
        </span>
      )}
    </div>
  );
}
