import Link from "next/link";
import { Fragment } from "react";
import type { Track } from "@/lib/types";

/**
 * The credited artists of a track as separate links, so a collaboration reaches every performer
 * rather than a single "A, B" page. Tracks stored before credits existed fall back to the
 * primary artist.
 *
 * Renders a wrapper element carrying the caller's class: the links inherit their colour from it
 * (see the global `a { color: inherit }`), so the surrounding styles keep working unchanged.
 */
export function ArtistLinks({
  track,
  className,
  onNavigate,
}: {
  track: Track;
  className?: string;
  onNavigate?: () => void;
}) {
  const credits =
    track.artists && track.artists.length > 0
      ? track.artists
      : [{ id: track.artistId, name: track.artistName }];

  return (
    <span className={className}>
      {credits.map((artist, index) => (
        <Fragment key={artist.id}>
          {index > 0 && ", "}
          <Link href={`/artists/${artist.id}`} onClick={onNavigate}>
            {artist.name}
          </Link>
        </Fragment>
      ))}
    </span>
  );
}
