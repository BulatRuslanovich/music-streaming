"use client";

import { useState } from "react";
import { coverUrl } from "@/lib/api";
import { accentFor, initialsFor } from "@/lib/format";
import { NoteIcon } from "./Icons";

interface CoverProps {
  /** Album id when known; the cover endpoint is served per album. */
  albumId?: string | null;
  /** Falls back to the track's own cover route when no album id is available. */
  trackId?: string | null;
  hasCover?: boolean;
  /** Used for the placeholder's initials and its deterministic colour. */
  name: string;
  size?: number | string;
  rounded?: boolean;
  className?: string;
}

/**
 * Album art with a graceful fallback. Covers are private, authenticated resources, so they are
 * plain `<img>` tags with credentials rather than Next's optimiser — the images live behind the
 * API and are already sized for their purpose.
 */
export function Cover({
  albumId,
  trackId,
  hasCover = true,
  name,
  size = "100%",
  rounded = false,
  className = "",
}: CoverProps) {
  const [failed, setFailed] = useState(false);

  const source = coverUrl({ albumId, trackId, hasCover });
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
        // eslint-disable-next-line @next/next/no-img-element
        <img src={source!} alt={`Cover of ${name}`} loading="lazy" onError={() => setFailed(true)} />
      ) : (
        <span className="cover-fallback" aria-hidden="true">
          {rounded ? initialsFor(name) : <NoteIcon size={22} />}
        </span>
      )}
    </div>
  );
}
