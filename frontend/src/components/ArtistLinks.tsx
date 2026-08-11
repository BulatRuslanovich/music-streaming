import Link from "next/link";
import { Fragment } from "react";
import type { Track } from "@/lib/types";

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
