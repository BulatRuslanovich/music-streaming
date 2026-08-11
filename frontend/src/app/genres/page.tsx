"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { TrackList } from "@/components/TrackList";
import { LoadError, PageHeader, PlayAllButton, Skeleton } from "@/components/ui";

export default function GenresPage() {
  const [selected, setSelected] = useState<string | null>(null);

  const genres = useApi(() => api.genres(), []);
  const tracks = useApi(
    () => (selected ? api.genreTracks(selected, { pageSize: 200 }) : Promise.resolve(null)),
    [selected],
  );

  const selectedGenre = genres.data?.find((genre) => genre.id === selected) ?? null;

  return (
    <>
      <PageHeader
        title="Genres"
        subtitle={genres.data ? `${genres.data.length} genres in your library` : undefined}
        actions={
          tracks.data && tracks.data.items.length > 0 ? (
            <PlayAllButton tracks={tracks.data.items} />
          ) : undefined
        }
      />

      {genres.error && <LoadError message={genres.error} onRetry={genres.reload} />}
      {genres.loading && !genres.data && <Skeleton count={8} />}

      {genres.data && genres.data.length === 0 && (
        <p className="empty-state">No genres yet — they come from the ID3 tags of your files.</p>
      )}

      {genres.data && genres.data.length > 0 && (
        <div className="chip-row">
          {genres.data.map((genre) => (
            <button
              key={genre.id}
              type="button"
              className={`chip ${selected === genre.id ? "is-active" : ""}`}
              onClick={() => setSelected(selected === genre.id ? null : genre.id)}
              aria-pressed={selected === genre.id}
            >
              {genre.name}
              <span className="chip-count">{genre.trackCount}</span>
            </button>
          ))}
        </div>
      )}

      {selectedGenre && (
        <section>
          <h2 className="section-title">{selectedGenre.name}</h2>
          {tracks.loading && !tracks.data && <Skeleton variant="row" count={6} />}
          {tracks.error && <LoadError message={tracks.error} onRetry={tracks.reload} />}
          {tracks.data && (
            <TrackList tracks={tracks.data.items} onChanged={tracks.reload} />
          )}
        </section>
      )}
    </>
  );
}
