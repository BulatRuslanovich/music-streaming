"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { formatTotalDuration } from "@/lib/format";
import { useApi } from "@/lib/useApi";
import { Cover } from "@/components/Cover";
import { TrackList } from "@/components/TrackList";
import { LoadError, PlayAllButton, Skeleton } from "@/components/ui";

export default function AlbumPage() {
  const params = useParams<{ id: string }>();
  const id = params.id;

  const { data, error, loading, reload } = useApi(() => api.album(id), [id]);

  if (error) return <LoadError message={error} onRetry={reload} />;
  if (loading && !data) return <Skeleton variant="row" count={8} />;
  if (!data) return null;

  return (
    <>
      <header className="detail-header">
        <div className="detail-art">
          <Cover albumId={data.id} hasCover={data.hasCover} name={data.title} />
        </div>

        <div className="detail-meta">
          <span className="detail-kind">Album</span>
          <h1>{data.title}</h1>
          <p className="detail-facts">
            <Link href={`/artists/${data.artistId}`} className="detail-artist">
              {data.artistName}
            </Link>
            {data.year ? <span> · {data.year}</span> : null}
            <span>
              {" "}
              · {data.tracks.length} track{data.tracks.length === 1 ? "" : "s"}
            </span>
            {data.durationSeconds > 0 && <span> · {formatTotalDuration(data.durationSeconds)}</span>}
          </p>

          <div className="detail-actions">
            <PlayAllButton tracks={data.tracks} label={`Play ${data.title}`} />
          </div>
        </div>
      </header>

      <TrackList
        tracks={data.tracks}
        showAlbum={false}
        showCover={false}
        showArtist
        useTrackNumbers
        onChanged={reload}
        emptyMessage="This album has no tracks."
      />
    </>
  );
}
