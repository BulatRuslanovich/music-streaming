"use client";

import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { Cover } from "@/components/Cover";
import { TrackList } from "@/components/TrackList";
import { AlbumCard, LoadError, PlayAllButton, SectionHeader, Skeleton } from "@/components/ui";

export default function ArtistPage() {
  const params = useParams<{ id: string }>();
  const id = params.id;

  const { data, error, loading, reload } = useApi(() => api.artist(id), [id]);

  if (error) return <LoadError message={error} onRetry={reload} />;
  if (loading && !data) return <Skeleton variant="row" count={8} />;
  if (!data) return null;

  return (
    <>
      <header className="detail-header">
        <div className="detail-art detail-art-round">
          <Cover name={data.name} hasCover={false} rounded />
        </div>

        <div className="detail-meta">
          <span className="detail-kind">Artist</span>
          <h1>{data.name}</h1>
          <p className="detail-facts">
            {data.albums.length} album{data.albums.length === 1 ? "" : "s"} · {data.tracks.length} track
            {data.tracks.length === 1 ? "" : "s"}
          </p>

          <div className="detail-actions">
            <PlayAllButton tracks={data.tracks} label={`Play ${data.name}`} />
          </div>
        </div>
      </header>

      {data.albums.length > 0 && (
        <section>
          <SectionHeader title="Albums" />
          <div className="card-grid">
            {data.albums.map((album) => (
              <AlbumCard key={album.id} album={album} />
            ))}
          </div>
        </section>
      )}

      <section>
        <SectionHeader title="Tracks" />
        <TrackList
          tracks={data.tracks}
          showArtist={false}
          onChanged={reload}
          emptyMessage="No tracks for this artist."
        />
      </section>
    </>
  );
}
